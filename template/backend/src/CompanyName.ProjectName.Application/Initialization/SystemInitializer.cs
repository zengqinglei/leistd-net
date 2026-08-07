#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#endif
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Users.Constants;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Options;
#if (IncludeRoles)
using Leistd.Authorization;
#endif
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
#if (IncludeOpenIddict)
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
#endif

namespace CompanyName.ProjectName.Application.Initialization;

/// <summary>
/// 系统初始化器实现
/// </summary>
public class SystemInitializer(
    IRepository<User, Guid> userRepository,
#if (IncludeRoles)
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
#endif
#if (IncludeIdentity)
    IPasswordHasher passwordHasher,
#endif
#if (IncludeRoles)
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
#endif
#if (IncludeOpenIddict)
    IOpenIddictScopeManager scopeManager,
#endif
    IOptions<DefaultAdminOptions> adminOptions,
    ILogger<SystemInitializer> logger) : ISystemInitializer
{
#if (IncludeRoles)
    private const string MemberRoleName = "Member";
#endif

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始初始化系统数据 ...");

#if (IncludeIdentity)
#if (IncludeRoles)
        var (adminRole, _) = await InitializeRolesAsync(cancellationToken);
#endif

        var adminUser = await InitializeDefaultAdminAsync(cancellationToken);
#if (IncludeRoles)
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);
#endif

#if (IncludeOpenIddict)
        await InitializeOpenIddictAsync(cancellationToken);
#endif

#if (IncludeRoles)
        await GrantAllPermissionsToAdminRoleAsync(adminRole, cancellationToken);
#endif
#else
        await InitializeIdentitylessAdminAsync(cancellationToken);
#endif
        logger.LogInformation("系统数据初始化完成");
    }

#if (IncludeRoles)
    private async Task<(Role AdminRole, Role MemberRole)> InitializeRolesAsync(CancellationToken cancellationToken)
    {
        var adminRole = await roleRepository.GetFirstAsync(r => r.Name == AdminConstant.RoleName, cancellationToken: cancellationToken);
        if (adminRole == null)
        {
            adminRole = new Role(
                name: AdminConstant.RoleName,
                displayName: "Administrator",
                description: "System administrator with all permissions",
                isStatic: true,
                isDefault: false,
                sort: 1
            );
            await roleRepository.InsertAsync(adminRole, cancellationToken);
            logger.LogInformation("已创建系统角色: {RoleName}", AdminConstant.RoleName);
        }

        var memberRole = await roleRepository.GetFirstAsync(r => r.Name == MemberRoleName, cancellationToken: cancellationToken);
        if (memberRole == null)
        {
            memberRole = new Role(
                name: MemberRoleName,
                displayName: "Member",
                description: "Default role automatically assigned to new users",
                isStatic: true,
                isDefault: true,
                sort: 100
            );
            await roleRepository.InsertAsync(memberRole, cancellationToken);
            logger.LogInformation("已创建系统角色: {RoleName}", MemberRoleName);
        }

        return (adminRole, memberRole);
    }

#endif
#if (IncludeIdentity)
    private async Task<User> InitializeDefaultAdminAsync(CancellationToken cancellationToken)
    {
        var options = adminOptions.Value;
        var adminUser = await userRepository.GetFirstAsync(u => u.IsSuperAdmin, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = await userRepository.GetFirstAsync(u => u.Username == options.Username, q => q.OrderBy(u => u.Id), cancellationToken);
            if (adminUser == null)
            {
                var passwordHash = passwordHasher.HashPassword(options.Password);
                adminUser = new User(
                    username: options.Username,
                    email: options.Email,
                    passwordHash: passwordHash,
                    displayName: options.DisplayName ?? "System Administrator"
                );

                adminUser.MarkAsSuperAdmin();
                await userRepository.InsertAsync(adminUser, cancellationToken);
                logger.LogInformation("已创建默认管理员用户: {Username}", options.Username);
            }
            else
            {
                adminUser.MarkAsSuperAdmin();
                await userRepository.UpdateAsync(adminUser, cancellationToken);
                logger.LogInformation("已将默认管理员用户标记为超级管理员: {Username}", adminUser.Username);
            }
        }
        else
        {
            logger.LogInformation("超级管理员用户已存在: {Username}", adminUser.Username);
        }

        return adminUser;
    }

#endif
#if (IncludeRoles)
    private async Task AssignAdminRoleAsync(User adminUser, Role adminRole, CancellationToken cancellationToken)
    {
        if (!await userRoleRepository.AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id, cancellationToken))
        {
            var userRole = new UserRole(adminUser.Id, adminRole.Id);
            await userRoleRepository.InsertAsync(userRole, cancellationToken);
            logger.LogInformation("已为管理员用户分配 {RoleName} 角色", AdminConstant.RoleName);
        }
    }

    /// <summary>
    /// 把当前全部权限定义幂等地授予 Admin 角色。
    /// </summary>
    /// <remarks>
    /// Admin 由此成为一个诚实的普通角色：初始拥有全部权限，但可被编辑、可被撤权，
    /// 系统中不存在"某个角色在代码里自动全权"的第二条旁路（唯一旁路是
    /// <c>User.IsSuperAdmin</c>）。每次启动都重新补齐，新增权限定义后重启即自愈。
    /// </remarks>
    private async Task GrantAllPermissionsToAdminRoleAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var definitions = permissionDefinitionManager
            .GetAll()
            .Where(definition => permissionDefinitionManager.IsEffectivelyEnabled(definition.Name))
            .Select(definition => definition.Name)
            .ToList();

        var providerKey = adminRole.Id.ToString();
        var existing = await permissionGrantStore.GetGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            cancellationToken);

        // 保留管理员已有的显式拒绝，只补齐缺失的允许，避免每次启动都覆盖运维的调整。
        var target = existing.Grants.ToDictionary(x => x.PermissionName, x => x.Effect, StringComparer.Ordinal);
        foreach (var name in definitions)
        {
            if (!target.ContainsKey(name))
            {
                target[name] = PermissionGrantEffect.Granted;
            }
        }

        if (target.Count == existing.Grants.Count)
        {
            logger.LogInformation("{RoleName} 角色权限已是最新，共 {Count} 项", adminRole.Name, target.Count);
            return;
        }

        await permissionGrantManager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            [.. target.Select(x => new PermissionGrant(x.Key, x.Value))],
            expectedRevision: null,
            cancellationToken);

        logger.LogInformation(
            "已为 {RoleName} 角色补齐权限授予，共 {Count} 项（该角色可被编辑与撤权，不存在代码级旁路）",
            adminRole.Name,
            target.Count);
    }

#endif
#if (IncludeOpenIddict)
    private async Task InitializeOpenIddictAsync(CancellationToken cancellationToken)
    {
        await EnsureScopeAsync(Scopes.OpenId, "OpenID", cancellationToken);
        await EnsureScopeAsync(Scopes.Profile, "Profile", cancellationToken);
        await EnsureScopeAsync(Scopes.Email, "Email", cancellationToken);
        await EnsureScopeAsync(Scopes.Roles, "Roles", cancellationToken);
        await EnsureScopeAsync(Scopes.OfflineAccess, "Offline access", cancellationToken);
    }

    private async Task EnsureScopeAsync(string name, string displayName, CancellationToken cancellationToken)
    {
        if (await scopeManager.FindByNameAsync(name, cancellationToken) != null)
            return;

        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = displayName
        }, cancellationToken);
    }
#endif
#if (!IncludeIdentity)
    private async Task InitializeIdentitylessAdminAsync(CancellationToken cancellationToken)
    {
        var options = adminOptions.Value;
        var adminUser = await userRepository.GetFirstAsync(u => u.IsSuperAdmin, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = await userRepository.GetFirstAsync(u => u.Username == options.Username, q => q.OrderBy(u => u.Id), cancellationToken);
            if (adminUser == null)
            {
                adminUser = new User(
                    username: options.Username,
                    email: options.Email,
                    displayName: options.DisplayName ?? "System Administrator"
                );

                await userRepository.InsertAsync(adminUser, cancellationToken);
                logger.LogInformation("已创建默认管理员用户: {Username}", options.Username);
            }
        }
        else
        {
            logger.LogInformation("超级管理员用户已存在: {Username}", adminUser.Username);
        }
    }
#endif
}
