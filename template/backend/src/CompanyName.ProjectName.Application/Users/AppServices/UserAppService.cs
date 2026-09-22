using Leistd.UnitOfWork.Attributes;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Users.Mappings;
using Leistd.Timing;
#endif
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Users.Avatars;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.Policies;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;

using Leistd.Security.Users;
using Leistd.ExceptionHandling;
using Leistd.ObjectMapping;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.Data.Paging;
#if (OpenIddictServer)
using OpenIddict.Abstractions;
#endif
#if (IncludeNotifications)
using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
#endif

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
    IOperationRecorder operationRecorder,
#if (LocalIdentity)
    UserSessionDomainService userSessionDomainService,
    ISecurityAlertPublisher securityAlerts,
#endif
#if (OpenIddictServer)
    IOpenIddictTokenManager tokenManager,
#endif
    ICurrentUser currentUser,
#if (IncludeNotifications)
    INotificationStore notificationStore,
#endif
    ILogger<UserAppService> logger,
#if (LocalIdentity)
    IClock clock,
#endif
    IObjectMapper objectMapper,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IUserAppService
{
    /// <summary>角色名称最大长度，与 Role 实体的持久化约束保持一致。</summary>
    private const int RoleNameMaxLength = 64;

    /// <summary>
    /// 获取用户列表（分页）
    /// </summary>
    public async Task<PagedResult<UserManagementOutputDto>> GetPagedListAsync(
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
                    return new PagedResult<UserManagementOutputDto>(0, []);
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
        return new PagedResult<UserManagementOutputDto>(totalCount, userDtos);
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

#if (LocalIdentity)
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
        AvatarPolicy.EnsureValid(input.Avatar?.Trim());
        var user = await userDomainService.CreateUserAsync(
            username, email, input.Password, displayName, cancellationToken: cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
        await userRepository.UpdateAsync(user, cancellationToken);
        var userRoles = await AssignRolesAsync(user.Id, roles, cancellationToken);

        logger.LogInformation("User created (ID: {Id})", user.Id);

        // 跟随本方法的 [UnitOfWork] 边界：建用户回滚，这条记录一并回滚，不留"记了但没发生"的假账
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserCreated,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.Create,
            cancellationToken);

        return MapToOutput(user, userRoles, roles);
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    /// <remarks>
    /// 资源服务形态下没有这个入口：用户名、邮箱、显示名归签发方所有，本地改了没有回写通道，
    /// 只会与签发方漂移。那一侧的资料由 <c>ResourceUserProvisioningMiddleware</c> 每次访问按令牌刷新。
    /// </remarks>
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

        // 编辑表单会把读到的头像地址原样送回，那表示"没改"，换回存储的原值再校验。
        var avatar = AvatarUrls.ResolveSubmitted(user, input.Avatar);
        AvatarPolicy.EnsureValid(avatar);

        // 启用状态原样带过：它只由 Enable/Disable 两个命令写入，那里才有"超管不得禁用自己"的保护。
        user.UpdateManagement(
            email,
            input.DisplayName?.Trim(),
            avatar,
            user.IsActive,
            input.IsEmailVerified);
        // 角色不在此处变更：普通资料更新与角色分配是两个命令、两个权限。
        await userRepository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("User updated (ID: {Id})", user.Id);

        // 补齐成功路径：此前只有 UserController 上的 [OperationRecordAction] 记被拒的更新，
        // 成功反而不留痕——而注解自己的文档写着"与成功路径使用的值逐字一致"，它预设了这里存在。
        // 本方法没有 [UnitOfWork]：写入即时生效，不随后续失败回滚。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserUpdated,
            OperationTarget.For(id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.Update,
            cancellationToken);

        return await MapToOutputAsync(user, cancellationToken);
    }

#endif
    /// <summary>
    /// 启用用户
    /// </summary>
    /// <remarks>两种形态都保留：即使身份由签发方发放，本服务仍要能就地停掉一个人的访问。</remarks>
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
#if (LocalIdentity)

        // 已在线的会话与已签发的令牌随之作废：登录时的启用检查挡不住它们
        await RevokeAllAccessAsync(user.Id, keepSessionId: null, cancellationToken);
#endif
    }

#if (LocalIdentity)
    /// <summary>
    /// 重置用户密码，并撤销该用户的全部会话
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

        // 密码被管理员重置，意味着账号可能已不在本人掌控之下：此前的会话全部作废。
        // 重置的是自己时保留当前这台，免得操作完把自己踢出去
        await RevokeAllAccessAsync(
            user.Id,
            user.Id == currentUser.Id ? currentUser.GetSessionId() : null,
            cancellationToken);
        await securityAlerts.PublishAsync(user.Id, new SecurityAlert(SecurityAlertKind.PasswordReset), cancellationToken);
    }

    /// <summary>
    /// 解除用户的登录锁定
    /// </summary>
    /// <remarks>
    /// 未锁定时静默成功、不留记录：解锁是幂等的，重复点击不该在操作记录里留下一串没发生过的事。
    /// </remarks>
    public async Task UnlockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        if (!user.IsLocked && user.AccessFailedCount == 0)
        {
            return;
        }

        user.Unlock();
        await userRepository.UpdateAsync(user, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserUnlocked,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.Update,
            cancellationToken);
    }

    /// <summary>
    /// 重置用户的两步验证：停用并清掉密钥与恢复码，该用户的会话全部失效
    /// </summary>
    /// <remarks>
    /// 给丢了手机又没了恢复码的人用。会话一并作废：能走到这一步，说明账号的第二道门已经不在本人手里。
    /// 所在租户要求两步验证时，本人下次登录会被带去重新设置。
    /// </remarks>
    public async Task ResetTwoFactorAsync(Guid id, CancellationToken cancellationToken = default)
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

        if (!user.TwoFactorEnabled)
        {
            return;
        }

        user.DisableTwoFactor();
        await userRepository.UpdateAsync(user, cancellationToken);
        await RevokeAllAccessAsync(
            user.Id,
            user.Id == currentUser.Id ? currentUser.GetSessionId() : null,
            cancellationToken);

        await securityAlerts.PublishAsync(user.Id, new SecurityAlert(SecurityAlertKind.TwoFactorReset), cancellationToken);
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserTwoFactorReset,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.Update,
            cancellationToken);
    }
#endif

#if (LocalIdentity)
    /// <summary>
    /// 删除用户（软删除）
    /// </summary>
    /// <remarks>
    /// 软删除保留该用户的直授权限与授权版本：软删除是可恢复的，恢复后权限跟着一起回来才合理；
    /// 删了权限再恢复，得到的是一个"存在但什么都不能做"的账号，没人会预期这个结果。
    /// 主体被永久删除时才调用 <c>IPermissionGrantManager.RemoveProviderAsync</c> 清理，
    /// 例如角色删除（<c>RoleAppService.DeleteAsync</c>）。
    /// <para>站内通知一并删掉：它们只对本人有意义，留下来就是没人能读、也没人能删的孤儿行。</para>
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

        await RevokeAllAccessAsync(user.Id, keepSessionId: null, cancellationToken);
        await userRepository.DeleteAsync(user, cancellationToken);
#if (IncludeNotifications)
        await notificationStore.DeleteAllAsync(id.ToString(), cancellationToken);
#endif
        logger.LogInformation("User deleted (ID: {Id})", id);

        // 名字在删除前就握在手里（user 变量即是）：删完再查什么都查不到，
        // 而审计要回答的正是"当时删掉的是谁"。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserDeleted,
            OperationTarget.For(id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.Delete,
            cancellationToken);
    }

#endif
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

    /// <inheritdoc />
    public async Task<UserAvatarOutputDto?> GetAvatarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        return user is not null && AvatarPolicy.TryReadImage(user.Avatar, out var image)
            ? new UserAvatarOutputDto(image.ContentType, image.Bytes)
            : null;
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

        // 本方法标了 [UnitOfWork]：这条记录跟随该工作单元，角色替换回滚则记录一并回滚——
        // 成功记录必须与它描述的那次变更同生共死。与下面的 UpdateAsync 不同，那里没有工作单元。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.UserRolesReplaced,
            OperationTarget.For(id, user.DisplayName ?? user.Username),
            PermissionConstant.Users.ManageRoles,
            cancellationToken);

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

    private Dictionary<string, object> CreateMappingContext(List<UserRole> userRoles, List<Role> roles)
    {
        return new Dictionary<string, object>
        {
            ["UserRoles"] = userRoles,
            ["Roles"] = roles,
#if (LocalIdentity)
            [UserProfile.NowKey] = clock.Now,
#endif
        };
    }

#if (LocalIdentity)
    /// <summary>
    /// 作废该用户已建立的会话与已签发的令牌。
    /// </summary>
    /// <remarks>
    /// 撤权要对已签发的凭据生效：会话 Cookie 由会话校验按登记的会话拒绝，Bearer 令牌由令牌记录校验按撤销状态拒绝，
    /// 两者都在认证阶段就失效。与写入同在一个工作单元里，撤不掉就整体失败，不留"账号停了、令牌还能用"的状态。
    /// </remarks>
    /// <param name="userId">用户 Id。</param>
    /// <param name="keepSessionId">保留的会话（管理员操作的是自己时保留当前这台），没有则为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RevokeAllAccessAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken)
    {
        await userSessionDomainService.RevokeAllAsync(userId, keepSessionId, cancellationToken);
#if (OpenIddictServer)
        // 逐个撤销而不是 RevokeBySubjectAsync：后者在 EF 存储里是批量 ExecuteUpdate，只有关系型提供程序支持，
        // 而未配连接串时本模板跑在 EF InMemory 上。先取全再逐个改，也避免边读边写占着同一个连接
        var tokens = await tokenManager.FindBySubjectAsync(userId.ToString(), cancellationToken).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            await tokenManager.TryRevokeAsync(token, cancellationToken);
        }
#endif
    }
#endif
}
