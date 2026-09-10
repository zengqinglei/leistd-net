using Leistd.UnitOfWork.Attributes;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;

using Leistd.Security.Users;
using Leistd.ExceptionHandling;
using Leistd.ObjectMapping;
using Leistd.Authorization.Abstractions;
using Leistd.ObjectMapping.Abstractions;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <summary>
/// 用户应用服务
/// </summary>
public class UserAppService(
    IRepository<User, Guid> userRepository,
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IPermissionChecker permissionChecker,
    UserDomainService userDomainService,
    ICurrentUser currentUser,
    ILogger<UserAppService> logger,
    IObjectMapper objectMapper,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IUserAppService
{
    /// <summary>角色名称最大长度，与 Role 实体的持久化约束保持一致。</summary>
    private const int RoleNameMaxLength = 64;

    /// <summary>
    /// 获取用户列表（分页）
    /// </summary>
    public async Task<PagedResultDto<UserManagementOutputDto>> GetPagedListAsync(
        GetUserPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var userQuery = await userRepository.GetQueryableAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            userQuery = userQuery.Where(u => u.Username.Contains(keyword) || u.Email.Contains(keyword) || (u.DisplayName != null && u.DisplayName.Contains(keyword)));
        }

        if (input.IsActive.HasValue)
        {
            userQuery = userQuery.Where(u => u.IsActive == input.IsActive.Value);
        }

#if (LocalIdentity)
        if (input.IsEmailVerified.HasValue)
        {
            userQuery = userQuery.Where(u => u.EmailConfirmed == input.IsEmailVerified.Value);
        }
#endif

        if (input.Roles is { Count: > 0 })
        {
            // 规范化：去 null/空白（模型绑定会把空白项转为 null）、去重；单项超长直接拒绝。
            var roleNames = input.Roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (roleNames.Exists(r => r.Length > RoleNameMaxLength))
            {
                throw new BadRequestException(
                    $"Role name cannot exceed {RoleNameMaxLength} characters.")
#if (IncludeLocalization)
                    .WithCode("Role:NameTooLong").WithData("MaximumLength", RoleNameMaxLength)
#endif
                    ;
            }
            if (roleNames.Count > 0)
            {
                var matchedRoles = await roleRepository.GetListAsync(r => roleNames.Contains(r.Name), cancellationToken);
                var roleIds = matchedRoles.Select(r => r.Id).ToList();
                if (roleIds.Count == 0)
                {
                    return new PagedResultDto<UserManagementOutputDto>(0, []);
                }

                var userRoleQuery = await userRoleRepository.GetQueryableAsync(cancellationToken);
                userQuery = userQuery.Where(u => userRoleQuery.Any(ur => roleIds.Contains(ur.RoleId) && ur.UserId == u.Id));
            }
        }

        var totalCount = await asyncExecuter.CountAsync(userQuery, cancellationToken);
        var users = await asyncExecuter.ToListAsync(
            ApplySorting(userQuery, input.Sorting).Skip(input.Offset).Take(input.Limit),
            cancellationToken);

        var userDtos = await MapToOutputsAsync(users, cancellationToken);
        return new PagedResultDto<UserManagementOutputDto>(totalCount, userDtos);
    }

    /// <summary>
    /// 用户列表的可排序字段
    /// </summary>
    /// <remarks>
    /// 白名单为什么在这一层见 <see cref="SortingRequest"/>。末尾固定追加 <c>Id</c> 是分页
    /// 正确性要求：排序键有重复值时，缺少稳定的次序会让同一行在翻页时重复出现或整行漏掉。
    /// </remarks>
    private static IQueryable<User> ApplySorting(IQueryable<User> query, string? sorting)
    {
        var (field, descending) = SortingRequest.Parse(sorting, "username");

        var ordered = field switch
        {
            "username" => SortingRequest.By(query, u => u.Username, descending),
            "email" => SortingRequest.By(query, u => u.Email, descending),
#if (LocalIdentity)
            "lastLoginTime" => SortingRequest.By(query, u => u.LastLoginTime, descending),
#endif
            "creationTime" => SortingRequest.By(query, u => u.CreationTime, descending),
            _ => throw SortingRequest.UnknownField(field)
        };

        return ordered.ThenBy(u => u.Id);
    }

    /// <summary>
    /// 获取用户详情
    /// </summary>
    public async Task<UserManagementOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        return await MapToOutputAsync(user, cancellationToken);
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    /// <remarks>建用户、补管理字段、分配角色三次写入必须同生共死：中途失败会留下没有任何角色的用户。</remarks>
    [UnitOfWork]
    public async Task<UserManagementOutputDto> CreateAsync(CreateUserInputDto input, CancellationToken cancellationToken = default)
    {
        var username = input.Username.Trim();
        var email = input.Email.Trim();
        var displayName = input.DisplayName?.Trim();

        logger.LogInformation("Creating user {Username} with email {Email}", username, email);

        // 创建时携带角色等同于一次角色分配，因此除创建权限外还必须持有 ManageRoles，
        // 否则只拥有创建权限的主体可以直接造出一个管理员账号。
        var roles = input.RoleIds.Count > 0
            ? await GetRolesWithManageRolesCheckAsync(input.RoleIds, cancellationToken)
            : await GetDefaultRolesAsync(cancellationToken);
#if (LocalIdentity)
        var user = await userDomainService.CreateUserAsync(
            username, email, input.Password, displayName, cancellationToken: cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
#else
        var user = await userDomainService.CreateUserAsync(input.SubjectId, username, email, displayName, cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, false);
#endif
        await userRepository.UpdateAsync(user, cancellationToken);
        var userRoles = await AssignRolesAsync(user.Id, roles, cancellationToken);

        logger.LogInformation("User created (ID: {Id})", user.Id);
        return MapToOutput(user, userRoles, roles);
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    public async Task<UserManagementOutputDto> UpdateAsync(Guid id, UpdateUserInputDto input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Updating user {Id}", id);

        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeManagedBy(currentUser.Id))
        {
            throw new BadRequestException("The built-in super administrator cannot be updated by other administrators.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminUpdateForbidden")
#endif
                ;
        }

        var email = input.Email.Trim();
        if (!await userDomainService.IsEmailAvailableAsync(id, email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithCode("User:EmailAlreadyUsed")
                .WithData("Email", email)
#endif
                ;
        }

        // 启用状态原样带过：它只由 Enable/Disable 两个命令写入，那里才有"超管不得禁用自己"的保护。
#if (LocalIdentity)
        user.UpdateManagement(
            email,
            input.DisplayName?.Trim(),
            input.Avatar?.Trim(),
            user.IsActive,
            input.IsEmailVerified);
#else
        user.UpdateManagement(email, input.DisplayName?.Trim(), input.Avatar?.Trim(), user.IsActive, false);
#endif
        // 角色不在此处变更：普通资料更新与角色分配是两个命令、两个权限。
        await userRepository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("User updated (ID: {Id})", user.Id);
        return await MapToOutputAsync(user, cancellationToken);
    }

    /// <summary>
    /// 启用用户
    /// </summary>
    public async Task EnableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeManagedBy(currentUser.Id))
        {
            throw new BadRequestException("The built-in super administrator cannot be operated on by other administrators.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminOperationForbidden")
#endif
                ;
        }

        user.Enable();
        await userRepository.UpdateAsync(user, cancellationToken);
    }

    /// <summary>
    /// 禁用用户
    /// </summary>
    public async Task DisableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeManagedBy(currentUser.Id))
        {
            throw new BadRequestException("The built-in super administrator cannot be disabled by other administrators.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminDisableForbidden")
#endif
                ;
        }
        if (!user.CanBeDisabled())
        {
            throw new BadRequestException("The built-in super administrator cannot disable itself.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminDisableSelfForbidden")
#endif
                ;
        }

        user.Disable();
        await userRepository.UpdateAsync(user, cancellationToken);
    }

#if (LocalIdentity)
    /// <summary>
    /// 重置用户密码
    /// </summary>
    public async Task ResetPasswordAsync(Guid id, ResetUserPasswordInputDto input, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeManagedBy(currentUser.Id))
        {
            throw new BadRequestException("The built-in super administrator's password cannot be reset by other administrators.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminResetPasswordForbidden")
#endif
                ;
        }

        userDomainService.ResetPassword(user, input.Password);
        await userRepository.UpdateAsync(user, cancellationToken);
    }
#endif

    /// <summary>
    /// 删除用户（软删除）
    /// </summary>
    /// <remarks>
    /// 软删除保留该用户的直授权限与授权版本：软删除是可恢复的，恢复后权限跟着一起回来才合理；
    /// 删了权限再恢复，得到的是一个"存在但什么都不能做"的账号，没人会预期这个结果。
    /// 主体被永久删除时才调用 <c>IPermissionGrantManager.RemoveProviderAsync</c> 清理，
    /// 例如角色删除（<c>RoleAppService.DeleteAsync</c>）。
    /// </remarks>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Deleting user {Id}", id);

        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeDeleted())
        {
            throw new BadRequestException("The built-in super administrator cannot be deleted.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminDeleteForbidden")
#endif
                ;
        }

        await userRepository.DeleteAsync(user, cancellationToken);
        logger.LogInformation("User deleted (ID: {Id})", id);
    }

    private async Task<User> GetUserOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"User {id} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", id)
#endif
                ;
        }

        return user;
    }

    /// <summary>
    /// 查询用户当前角色。
    /// </summary>
    public async Task<IReadOnlyList<RoleBriefDto>> GetRolesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await GetUserOrThrowAsync(id, cancellationToken);

        var userRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == id, cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();
        return ToRoleBriefs(roles);
    }

    /// <summary>
    /// 替换用户角色。调用方必须持有 App.Users.ManageRoles，由 Controller 上的策略保证。
    /// </summary>
    /// <remarks>先删旧角色再插新角色：拆成两次提交时，插入失败会把用户留在零角色状态。</remarks>
    [UnitOfWork]
    public async Task<IReadOnlyList<RoleBriefDto>> ReplaceRolesAsync(
        Guid id,
        UpdateUserRolesInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.CanBeManagedBy(currentUser.Id))
        {
            throw new BadRequestException("The built-in super administrator cannot be updated by other administrators.")
#if (IncludeLocalization)
                .WithCode("User:SuperAdminUpdateForbidden")
#endif
                ;
        }

        var roles = await GetRolesByIdsAsync(input.RoleIds, cancellationToken);
        await ReplaceUserRolesAsync(id, roles, cancellationToken);

        logger.LogInformation("User roles replaced (ID: {Id}, role count: {Count})", id, roles.Count);
        return ToRoleBriefs(roles);
    }

    /// <summary>
    /// 解析角色前先确认调用方持有角色分配权限。
    /// </summary>
    private async Task<List<Role>> GetRolesWithManageRolesCheckAsync(
        List<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.Users.ManageRoles, cancellationToken))
        {
            throw new ForbiddenException("Assigning roles requires the user role management permission.")
#if (IncludeLocalization)
                .WithCode("User:ManageRolesRequired")
#endif
                ;
        }

        return await GetRolesByIdsAsync(roleIds, cancellationToken);
    }

    /// <summary>
    /// 按 Id 解析角色。角色名只用于展示与筛选，写入路径一律按 Id，避免大小写与重名歧义。
    /// </summary>
    private async Task<List<Role>> GetRolesByIdsAsync(List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var normalized = roleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (normalized.Count == 0)
        {
            return [];
        }

        var roles = (await roleRepository.GetListAsync(r => normalized.Contains(r.Id), cancellationToken)).ToList();
        var missing = normalized.Except(roles.Select(r => r.Id)).ToList();
        if (missing.Count != 0)
        {
            throw new BadRequestException($"Roles not found: {string.Join(", ", missing)}")
#if (IncludeLocalization)
                .WithCode("User:RolesNotFound")
                .WithData("Roles", string.Join(", ", missing))
#endif
                ;
        }

        return roles;
    }

    /// <summary>
    /// 没有显式指定角色时使用默认角色，保证新用户不会处于"零角色"状态。
    /// </summary>
    private async Task<List<Role>> GetDefaultRolesAsync(CancellationToken cancellationToken)
        => [.. await roleRepository.GetListAsync(r => r.IsDefault, cancellationToken)];

    /// <returns>本次写入的用户角色关联行。</returns>
    /// <remarks>
    /// 回传而不是让调用方回查：在工作单元内这些行还没落库，回查查不到。
    /// 调用方要的本来就是"我刚写进去的那些"，回查多一次往返还多一层不确定。
    /// </remarks>
    private async Task<List<UserRole>> AssignRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        var userRoles = roles.Select(role => new UserRole(userId, role.Id)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);
        return userRoles;
    }

    /// <returns>替换后的用户角色关联行。</returns>
    /// <remarks><inheritdoc cref="AssignRolesAsync" path="/remarks"/></remarks>
    private async Task<List<UserRole>> ReplaceUserRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
    {
        var currentRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == userId, cancellationToken)).ToList();
        if (currentRoles.Count != 0)
        {
            await userRoleRepository.DeleteManyAsync(currentRoles, cancellationToken);
        }

        return await AssignRolesAsync(userId, roles, cancellationToken);
    }

    private async Task<List<UserManagementOutputDto>> MapToOutputsAsync(List<User> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var userIds = users.Select(u => u.Id).ToList();
        var userRoles = (await userRoleRepository.GetListAsync(ur => userIds.Contains(ur.UserId), cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();

        return objectMapper.Map<List<User>, List<UserManagementOutputDto>>(users, CreateMappingContext(userRoles, roles));
    }

    private async Task<UserManagementOutputDto> MapToOutputAsync(User user, CancellationToken cancellationToken)
    {
        var userRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == user.Id, cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();
        return objectMapper.Map<User, UserManagementOutputDto>(user, CreateMappingContext(userRoles, roles));
    }

    /// <remarks>
    /// 供刚写入的路径使用：角色数据由调用方给出，不回查数据库。
    /// 写方法本来就知道自己产出了什么，回查既多一次往返，又要求那些行已经落库。
    /// </remarks>
    private UserManagementOutputDto MapToOutput(User user, List<UserRole> userRoles, List<Role> roles)
    {
        return objectMapper.Map<User, UserManagementOutputDto>(user, CreateMappingContext(userRoles, roles));
    }

    /// <remarks>排序是展示口径，映射交给已注册的 <c>Role → RoleBriefDto</c>，不在此手工构造 DTO。</remarks>
    private List<RoleBriefDto> ToRoleBriefs(List<Role> roles)
    {
        var ordered = roles
            .OrderBy(r => r.Sort)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return objectMapper.Map<List<Role>, List<RoleBriefDto>>(ordered);
    }

    private static Dictionary<string, object> CreateMappingContext(List<UserRole> userRoles, List<Role> roles)
    {
        return new Dictionary<string, object>
        {
            ["UserRoles"] = userRoles,
            ["Roles"] = roles
        };
    }

}
