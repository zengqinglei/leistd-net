#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
#endif
#endif
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Options;
#endif
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Abstractions;
using Leistd.Lock;
using Leistd.Lock.Abstractions;
#if (OpenIddictServer)
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
#endif

namespace CompanyName.ProjectName.Application.Initialization;

/// <summary>
/// 系统初始化器实现
/// </summary>
public class SystemInitializer(
#if (LocalIdentity)
    IRepository<User, Guid> userRepository,
#endif
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
#if (LocalIdentity)
    UserDomainService userDomainService,
#endif
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
#if (OpenIddictServer)
    IOpenIddictScopeManager scopeManager,
#endif
#if (LocalIdentity)
    IOptions<DefaultAdminOptions> adminOptions,
#endif
    IDistributedLock distributedLock,
    ILogger<SystemInitializer> logger) : ISystemInitializer
{
    /// <summary>
    /// 初始化互斥锁的键。
    /// </summary>
    /// <remarks>
    /// 公开是为了让集成测试能对同一把锁断言互斥，而不是各写一份字面量。
    /// </remarks>
    public const string InitializationLockKey = "MyProject:system-initialization";

    private const string MemberRoleName = "Member";

    /// <remarks>
    /// 初始化在 <see cref="IDistributedLock"/> 内串行执行，避免多实例并发创建角色、用户、
    /// OIDC 客户端或权限授予。多副本部署必须使用跨实例锁实现；单副本可使用内存实现。
    /// <see cref="ILockHandle.LockLost"/> 会取消后续写入，防止失去租约的实例继续与新持有者并发初始化。
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var lockHandle = await distributedLock.LockAsync(InitializationLockKey, cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lockHandle.LockLost);

        cancellationToken = lockScope.Token;

        logger.LogInformation("Initializing system data...");

#if (LocalIdentity)
        var (adminRole, _) = await InitializeRolesAsync(cancellationToken);
        await SeedAdminRolePermissionsAsync(adminRole, cancellationToken);

        var adminUser = await InitializeDefaultAdminAsync(cancellationToken);
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);

#if (OpenIddictServer)
        await InitializeOpenIddictAsync(cancellationToken);
#endif

#else
        _ = await InitializeRolesAsync(cancellationToken);
#endif
        logger.LogInformation("System data initialization completed");
    }

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
            logger.LogInformation("System role created: {RoleName}", AdminConstant.RoleName);
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
            logger.LogInformation("System role created: {RoleName}", MemberRoleName);
        }

        return (adminRole, memberRole);
    }

#if (LocalIdentity)
    private async Task<User> InitializeDefaultAdminAsync(CancellationToken cancellationToken)
    {
        var options = adminOptions.Value;
        var adminUser = await userRepository.GetFirstAsync(u => u.IsSuperAdmin, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = await userRepository.GetFirstAsync(u => u.Username == options.Username, q => q.OrderBy(u => u.Id), cancellationToken);
            if (adminUser == null)
            {
                // 实体创建的核心逻辑（口令策略、哈希、超管标记、落库）在领域服务里，
                // 应用层只负责判断"是否需要创建"
                adminUser = await userDomainService.CreateSuperAdminAsync(
                    options.Username,
                    options.Email,
                    options.Password!,
                    options.DisplayName ?? "System Administrator",
                    passwordSubject: $"{DefaultAdminOptions.SectionName}:Password",
                    cancellationToken);
                logger.LogInformation("Default admin user created: {Username}", options.Username);
            }
            else
            {
                adminUser.MarkAsSuperAdmin();
                await userRepository.UpdateAsync(adminUser, cancellationToken);
                logger.LogInformation("Default admin user marked as super admin: {Username}", adminUser.Username);
            }
        }
        else
        {
            logger.LogInformation("Super admin user already exists: {Username}", adminUser.Username);
        }

        return adminUser;
    }

#endif
    private async Task AssignAdminRoleAsync(User adminUser, Role adminRole, CancellationToken cancellationToken)
    {
        if (!await userRoleRepository.AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id, cancellationToken))
        {
            var userRole = new UserRole(adminUser.Id, adminRole.Id);
            await userRoleRepository.InsertAsync(userRole, cancellationToken);
            logger.LogInformation("Assigned role {RoleName} to the admin user", AdminConstant.RoleName);
        }
    }

    /// <summary>
    /// 在 Admin 角色尚未有过任何授予写入时，把当前全部权限定义播种给它。
    /// </summary>
    /// <remarks>
    /// 授权版本为 0 表示从未写入授予，可覆盖角色已创建但播种中断的状态。
    /// 首次播种后 Admin 按普通角色管理：权限可撤销，启动过程不会自动补回缺失权限。
    /// 新增权限由管理员显式授予；宿主超级管理员负责避免权限管理被锁死。
    /// </remarks>
    private async Task SeedAdminRolePermissionsAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var providerKey = adminRole.Id.ToString();
        var existing = await permissionGrantStore.GetGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            cancellationToken);

        if (existing.Version != 0)
        {
            return;
        }

        var definitions = permissionDefinitionManager
            .GetAll()
            .Where(definition => permissionDefinitionManager.IsEffectivelyEnabled(definition.Name))
            .Select(definition => definition.Name)
            .ToList();

        await permissionGrantManager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            definitions,
            expectedVersion: existing.Version,
            cancellationToken);

        logger.LogInformation(
            "Seeded permission grants for role {RoleName}: {Count} item(s) (the role can be edited and revoked afterwards; grants are not auto-replenished)",
            adminRole.Name,
            definitions.Count);
    }

#if (OpenIddictServer)
    private async Task InitializeOpenIddictAsync(CancellationToken cancellationToken)
    {
        await EnsureScopeAsync(Scopes.OpenId, "OpenID", cancellationToken);
        await EnsureScopeAsync(Scopes.Profile, "Profile", cancellationToken);
        await EnsureScopeAsync(Scopes.Email, "Email", cancellationToken);
        await EnsureScopeAsync(Scopes.Roles, "Roles", cancellationToken);
        await EnsureScopeAsync(Scopes.OfflineAccess, "Offline access", cancellationToken);
        await EnsureScopeAsync(
            TenantConnectionScopes.RuntimeRead,
            "Read tenant connection routing metadata",
            cancellationToken);
        await EnsureScopeAsync(
            TenantConnectionScopes.MigrationRead,
            "Read tenant connection migration metadata",
            cancellationToken);
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
}
