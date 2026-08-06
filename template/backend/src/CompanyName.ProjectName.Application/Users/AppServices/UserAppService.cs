using System.Linq.Dynamic.Core;
using CompanyName.ProjectName.Application.Users.Dtos;
#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#endif
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
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
#if (IncludeIdentity)
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IPasswordHasher passwordHasher,
#endif
    UserDomainService userDomainService,
    ICurrentUser currentUser,
    ILogger<UserAppService> logger,
    IObjectMapper objectMapper,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IUserAppService
{
#if (IncludeIdentity)
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
        var roles = await GetRolesByNamesAsync(input.Roles, cancellationToken);
        var user = await userDomainService.CreateUserAsync(username, email, input.Password, displayName, cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
#else
        var user = await userDomainService.CreateUserAsync(username, email, "", displayName, cancellationToken);
        user.UpdateManagement(email, displayName, input.Avatar?.Trim(), input.IsActive, false);
#endif
        await userRepository.UpdateAsync(user, cancellationToken);
#if (IncludeIdentity)
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
        var roles = await GetRolesByNamesAsync(input.Roles, cancellationToken);
        user.UpdateManagement(email, input.DisplayName?.Trim(), input.Avatar?.Trim(), input.IsActive, input.IsEmailVerified);
#else
        user.UpdateManagement(email, input.DisplayName?.Trim(), input.Avatar?.Trim(), input.IsActive, false);
#endif
        await userRepository.UpdateAsync(user, cancellationToken);
#if (IncludeIdentity)
        await ReplaceRolesAsync(user.Id, roles, cancellationToken);
#endif

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

#if (IncludeIdentity)
    private async Task<List<Role>> GetRolesByNamesAsync(List<string> roleNames, CancellationToken cancellationToken)
    {
        var normalizedRoleNames = roleNames
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRoleNames.Count == 0)
        {
            throw new BadRequestException("Please select at least one role.")
#if (IncludeLocalization)
                .WithLocalization("User:RoleRequired")
#endif
                ;
        }

        var roles = (await roleRepository.GetListAsync(r => normalizedRoleNames.Contains(r.Name), cancellationToken)).ToList();
        var missingRoles = normalizedRoleNames.Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (missingRoles.Count != 0)
        {
            throw new BadRequestException($"Roles not found: {string.Join(", ", missingRoles)}")
#if (IncludeLocalization)
                .WithLocalization("User:RolesNotFound")
                .WithData("Roles", string.Join(", ", missingRoles))
#endif
                ;
        }

        return roles;
    }

    private async Task AssignRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
    {
        var userRoles = roles.Select(role => new UserRole(userId, role.Id)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);
    }

    private async Task ReplaceRolesAsync(Guid userId, List<Role> roles, CancellationToken cancellationToken)
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

#if (IncludeIdentity)
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
#if (IncludeIdentity)
        var userRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == user.Id, cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).Distinct().ToList();
        var roles = roleIds.Count == 0 ? [] : (await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken)).ToList();
        return objectMapper.Map<User, UserManagementOutputDto>(user, CreateMappingContext(userRoles, roles));
#else
        return objectMapper.Map<User, UserManagementOutputDto>(user);
#endif
    }

#if (IncludeIdentity)
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
