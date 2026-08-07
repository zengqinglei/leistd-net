using System.Linq.Dynamic.Core;
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
#endif
using CompanyName.ProjectName.Application.Users.Dtos;
#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#endif
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
#if (IncludeRoles)
using Leistd.Authorization;
#endif
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Leistd.ObjectMapping.Core;
using Microsoft.Extensions.Logging;

using Leistd.Security.Users;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <summary>
/// 用户应用服务
/// </summary>
public class UserAppService(
    IRepository<User, Guid> userRepository,
#if (IncludeRoles)
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
#endif
#if (IncludeIdentity)
    IPasswordHasher passwordHasher,
#endif
#if (IncludeRoles)
    IPermissionChecker permissionChecker,
#endif
    UserDomainService userDomainService,
    ICurrentUser currentUser,
    ILogger<UserAppService> logger,
    IObjectMapper objectMapper,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IUserAppService
{
#if (IncludeRoles)
    /// <summary>角色名称最大长度，与 Role 实体的持久化约束保持一致。</summary>
    private const int RoleNameMaxLength = 64;

#endif
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

#if (IncludeIdentity)
        if (input.IsEmailVerified.HasValue)
        {
            userQuery = userQuery.Where(u => u.EmailConfirmed == input.IsEmailVerified.Value);
        }
#endif
#if (IncludeRoles)

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
                    $"Role name cannot exceed {RoleNameMaxLength} characters.");
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
#endif

        var totalCount = await asyncExecuter.CountAsync(userQuery, cancellationToken);
        var sorting = input.Sorting ?? "username asc";
        userQuery = userQuery.OrderBy(sorting);
        var users = await asyncExecuter.ToListAsync(
            userQuery.Skip(input.Offset).Take(input.Limit),
            cancellationToken);

        var userDtos = await MapToOutputsAsync(users, cancellationToken);
        return new PagedResultDto<UserManagementOutputDto>(totalCount, userDtos);
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
    public async Task<UserManagementOutputDto> CreateAsync(CreateUserInputDto input, CancellationToken cancellationToken = default)
    {
        var username = input.Username.Trim();
        var email = input.Email.Trim();
        var displayName = input.DisplayName?.Trim();

        logger.LogInformation("开始创建用户 {Username}... 邮箱：{Email}", username, email);

#if (IncludeIdentity)
#if (IncludeRoles)
        // 创建时携带角色等同于一次角色分配，因此除创建权限外还必须持有 ManageRoles，
        // 否则只拥有创建权限的主体可以直接造出一个管理员账号。
        var roles = input.RoleIds.Count > 0
            ? await GetRolesWithManageRolesCheckAsync(input.RoleIds, cancellationToken)
            : await GetDefaultRolesAsync(cancellationToken);
#endif
        var user = await userDomainService.CreateUserAsync(username, email, input.Password, displayName, cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
#else
        var user = await userDomainService.CreateUserAsync(username, email, "", displayName, cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, false);
#endif
        await userRepository.UpdateAsync(user, cancellationToken);
#if (IncludeRoles)
        await AssignRolesAsync(user.Id, roles, cancellationToken);
#endif

        logger.LogInformation("创建用户成功 (ID: {Id})", user.Id);
        return await MapToOutputAsync(user, cancellationToken);
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    public async Task<UserManagementOutputDto> UpdateAsync(Guid id, UpdateUserInputDto input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始更新用户 {Id}...", id);

        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (user.IsSuperAdmin && user.Id != currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator cannot be updated by other administrators.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminUpdateForbidden")
#endif
                ;
        }

        var email = input.Email.Trim();
        if (!await userDomainService.IsEmailAvailableAsync(id, email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithLocalization("User:EmailAlreadyUsed")
                .WithData("Email", email)
#endif
                ;
        }

#if (IncludeIdentity)
        user.UpdateManagement(email, input.DisplayName?.Trim(), input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
#else
        user.UpdateManagement(email, input.DisplayName?.Trim(), input.Avatar?.Trim(), input.IsActive, false);
#endif
        // 角色不在此处变更：普通资料更新与角色分配是两个命令、两个权限。
        await userRepository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("更新用户成功 (ID: {Id})", user.Id);
        return await MapToOutputAsync(user, cancellationToken);
    }

    /// <summary>
    /// 启用用户
    /// </summary>
    public async Task EnableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (user.IsSuperAdmin && user.Id != currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator cannot be operated on by other administrators.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminOperationForbidden")
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
        if (user.IsSuperAdmin && user.Id != currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator cannot be disabled by other administrators.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminDisableForbidden")
#endif
                ;
        }
        if (user.IsSuperAdmin && user.Id == currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator cannot disable itself.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminDisableSelfForbidden")
#endif
                ;
        }

        user.Disable();
        await userRepository.UpdateAsync(user, cancellationToken);
    }

#if (IncludeIdentity)
    /// <summary>
    /// 重置用户密码
    /// </summary>
    public async Task ResetPasswordAsync(Guid id, ResetUserPasswordInputDto input, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (user.IsSuperAdmin && user.Id != currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator's password cannot be reset by other administrators.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminResetPasswordForbidden")
#endif
                ;
        }

        user.UpdatePasswordHash(passwordHasher.HashPassword(input.Password));
        await userRepository.UpdateAsync(user, cancellationToken);
    }
#endif

    /// <summary>
    /// 删除用户（软删除）
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始删除用户 {Id}...", id);

        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (user.IsSuperAdmin)
        {
            throw new BadRequestException("The built-in super administrator cannot be deleted.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminDeleteForbidden")
#endif
                ;
        }

        await userRepository.DeleteAsync(user, cancellationToken);
        logger.LogInformation("删除用户成功 (ID: {Id})", id);
    }

    private async Task<User> GetUserOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"User {id} not found.")
#if (IncludeLocalization)
                .WithLocalization("User:NotFound")
                .WithData("Id", id)
#endif
                ;
        }

        return user;
    }

#if (IncludeRoles)
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
        return [.. roles
            .OrderBy(r => r.Sort)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new RoleBriefDto { Id = r.Id, Name = r.Name, DisplayName = r.DisplayName })];
    }

    /// <summary>
    /// 替换用户角色。调用方必须持有 App.Users.ManageRoles，由 Controller 上的策略保证。
    /// </summary>
    public async Task<IReadOnlyList<RoleBriefDto>> ReplaceRolesAsync(
        Guid id,
        UpdateUserRolesInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (user.IsSuperAdmin && user.Id != currentUser.Id)
        {
            throw new BadRequestException("The built-in super administrator cannot be updated by other administrators.")
#if (IncludeLocalization)
                .WithLocalization("User:SuperAdminUpdateForbidden")
#endif
                ;
        }

        var roles = await GetRolesByIdsAsync(input.RoleIds, cancellationToken);
        await ReplaceUserRolesAsync(id, roles, cancellationToken);

        logger.LogInformation("替换用户角色成功 (ID: {Id}，角色数: {Count})", id, roles.Count);
        return await GetRolesAsync(id, cancellationToken);
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
                .WithLocalization("User:ManageRolesRequired")
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
                .WithLocalization("User:RolesNotFound")
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

    private async Task AssignRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
        {
            return;
        }

        var userRoles = roles.Select(role => new UserRole(userId, role.Id)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);
    }

    private async Task ReplaceUserRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
    {
        var currentRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == userId, cancellationToken)).ToList();
        if (currentRoles.Count != 0)
        {
            await userRoleRepository.DeleteManyAsync(currentRoles, cancellationToken);
        }

        await AssignRolesAsync(userId, roles, cancellationToken);
    }
#endif

    private async Task<List<UserManagementOutputDto>> MapToOutputsAsync(List<User> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

#if (IncludeRoles)
        var userIds = users.Select(u => u.Id).ToList();
        var userRoles = (await userRoleRepository.GetListAsync(ur => userIds.Contains(ur.UserId), cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();

        return objectMapper.Map<List<User>, List<UserManagementOutputDto>>(users, CreateMappingContext(userRoles, roles));
#else
        return objectMapper.Map<List<User>, List<UserManagementOutputDto>>(users);
#endif
    }

    private async Task<UserManagementOutputDto> MapToOutputAsync(User user, CancellationToken cancellationToken)
    {
#if (IncludeRoles)
        var userRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == user.Id, cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();
        return objectMapper.Map<User, UserManagementOutputDto>(user, CreateMappingContext(userRoles, roles));
#else
        return objectMapper.Map<User, UserManagementOutputDto>(user);
#endif
    }

#if (IncludeRoles)
    private static Dictionary<string, object> CreateMappingContext(List<UserRole> userRoles, List<Role> roles)
    {
        return new Dictionary<string, object>
        {
            ["UserRoles"] = userRoles,
            ["Roles"] = roles
        };
    }
#endif

}
