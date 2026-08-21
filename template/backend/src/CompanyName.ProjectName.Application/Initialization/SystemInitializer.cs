#if (IdentityService)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Application.TenantConnections;
#endif
#if (LocalAuthorization)
using CompanyName.ProjectName.Domain.Users.Constants;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Options;
#if (LocalAuthorization)
using Leistd.Authorization;
#endif
using Leistd.Ddd.Domain.Repositories;
using Leistd.Lock.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
#if (IdentityService)
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
#endif

namespace CompanyName.ProjectName.Application.Initialization;

/// <summary>
/// 系统初始化器实现
/// </summary>
public class SystemInitializer(
#if (IdentityService)
    IRepository<User, Guid> userRepository,
#endif
#if (LocalAuthorization)
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
#endif
#if (IdentityService)
    IPasswordHasher passwordHasher,
#endif
#if (LocalAuthorization)
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
#endif
#if (IdentityService)
    IOpenIddictScopeManager scopeManager,
#endif
#if (IdentityService)
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

#if (LocalAuthorization)
    private const string MemberRoleName = "Member";
#endif

    /// <remarks>
    /// 整个初始化在 <see cref="IDistributedLock"/> 内串行执行。这里每一步都是"先查存在、
    /// 不存在再建"，单实例下幂等，多实例同时启动就全是竞争窗口：角色名、用户名、
    /// OpenIddict 客户端各自的唯一索引会冲突，权限播种会撞上授权版本冲突，异常一路冒泡穿过
    /// <c>ApplicationBootstrapper</c>，落败的那个实例直接起不来。加锁之后落败方是排队而不是撞车：
    /// 等前一个做完，再把同一套幂等检查走一遍，发现该建的都在、版本已大于 0，全部跳过。
    ///
    /// 只在权限播种处捕获冲突并不够——角色创建的窗口更靠前，堵了后面也走不到。
    ///
    /// 锁的实际作用范围由部署决定：多副本部署必须配置 Redis（<c>AddRedisDistributedLock</c>），
    /// 单副本走内存实现即可。入口统一为 <see cref="IDistributedLock"/>，业务代码不因部署形态而变。
    ///
    /// 初始化可能长时间持锁（迁移、播种、外部依赖抖动），而基于租约的实现无法保证"拿到锁就一直持有"。
    /// 因此把 <see cref="ILockHandle.LockLost"/> 并进本次的取消令牌：一旦失去持锁资格，
    /// 后续操作立即中止，由启动失败暴露出来，而不是与新的持有者同时往同一套数据里写。
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var lockHandle = await distributedLock.LockAsync(InitializationLockKey, cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lockHandle.LockLost);

        cancellationToken = lockScope.Token;

        logger.LogInformation("开始初始化系统数据 ...");

#if (IdentityService)
#if (LocalAuthorization)
        var (adminRole, _) = await InitializeRolesAsync(cancellationToken);
        await SeedAdminRolePermissionsAsync(adminRole, cancellationToken);
#endif

        var adminUser = await InitializeDefaultAdminAsync(cancellationToken);
#if (LocalAuthorization)
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);
#endif

#if (IdentityService)
        await InitializeOpenIddictAsync(cancellationToken);
#endif

#else
#if (LocalAuthorization)
        _ = await InitializeRolesAsync(cancellationToken);
#endif
#endif
        logger.LogInformation("系统数据初始化完成");
    }

#if (LocalAuthorization)
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
#if (IdentityService)
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
#if (LocalAuthorization)
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
    /// 在 Admin 角色尚未有过任何授予写入时，把当前全部权限定义播种给它。
    /// </summary>
    /// <remarks>
    /// 判据是授权版本为 0，而不是"本次新建了角色"。初始化没有事务，角色是立即落库的，
    /// 因此"角色已建、权限未播"是可达状态；只认"本次新建"的话，那一次中断会让 Admin 永久缺权限。
    /// 版本为 0 精确表示"从未写过授予"，既涵盖刚创建，也涵盖上次中断；而人工清空权限会把版本推到
    /// 大于 0，不会被误当成未播种再补回来。
    ///
    /// 只播种一次，之后 Admin 就是一个诚实的普通角色：可编辑、可撤权，代码里不存在
    /// "某个角色自动全权"的第二条旁路（唯一旁路是 <c>User.IsSuperAdmin</c>）。
    ///
    /// 不在每次启动时补齐缺失权限：纯加法模型无法区分"版本升级新增的定义"与"管理员明确撤销的权限"，
    /// 补齐必然把人工撤权又加回来，"可撤权"就成了空话。升级后新增的权限由管理员显式授予，
    /// 期间超级管理员凭 <c>IsSuperAdmin</c> 旁路照常可用，不存在把人锁在门外的风险。
    /// </remarks>
    private async Task SeedAdminRolePermissionsAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var providerKey = adminRole.Id.ToString();
        var existing = await permissionGrantStore.GetGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            cancellationToken);

        if (existing.Revision != 0)
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
            expectedRevision: existing.Revision,
            cancellationToken);

        logger.LogInformation(
            "已为 {RoleName} 角色播种权限授予，共 {Count} 项（此后该角色可被编辑与撤权，不再自动补齐）",
            adminRole.Name,
            definitions.Count);
    }

#endif
#if (IdentityService)
    private async Task InitializeOpenIddictAsync(CancellationToken cancellationToken)
    {
        await EnsureScopeAsync(Scopes.OpenId, "OpenID", cancellationToken);
        await EnsureScopeAsync(Scopes.Profile, "Profile", cancellationToken);
        await EnsureScopeAsync(Scopes.Email, "Email", cancellationToken);
#if (LocalAuthorization)
        await EnsureScopeAsync(Scopes.Roles, "Roles", cancellationToken);
#endif
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
