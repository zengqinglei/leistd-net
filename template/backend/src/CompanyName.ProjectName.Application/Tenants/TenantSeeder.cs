#if (IncludeTenancy)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Lock.Core;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Tenants;

/// <summary>
/// 租户初始化种子实现
/// </summary>
/// <remarks>
/// 与宿主侧 <c>SystemInitializer</c> 的判据一致：角色"先查后建"，权限播种以授权版本为 0
/// 为准（精确表示"从未写过授予"，涵盖首建与上次中断，不会把人工撤权补回来）。
/// 所有仓储与授予读写都在调用方建立的租户上下文内自动分区。
/// 互斥锁按租户隔离（锁 key 含 tenantId），不同租户的种子互不排队。
/// </remarks>
public class TenantSeeder(
    ICurrentTenant currentTenant,
    IRepository<User, Guid> userRepository,
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IPasswordHasher passwordHasher,
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
    IDistributedLock distributedLock,
    ILogger<TenantSeeder> logger) : ITenantSeeder
{
    private const string MemberRoleName = "Member";
    private const string TenantAdminUsername = "admin";

    /// <inheritdoc />
    public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
    {
        if (currentTenant.Id is not { } tenantId)
        {
            throw new InvalidOperationException("租户种子必须在租户上下文内执行：先 ICurrentTenant.Change(tenantId) 再调用。");
        }

        // 锁 key 含租户 Id：租户级互斥操作的约定，不同租户互不排队
        await using var lockHandle = await distributedLock.LockAsync($"MyProject:tenant-init:{tenantId}", cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lockHandle.LockLost);
        cancellationToken = lockScope.Token;

        logger.LogInformation("开始初始化租户 {TenantId} ...", tenantId);

        var adminRole = await EnsureRolesAsync(cancellationToken);
        await SeedAdminRolePermissionsAsync(adminRole, cancellationToken);
        var adminUser = await EnsureTenantAdminAsync(adminEmail, adminPassword, cancellationToken);
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);

        logger.LogInformation("租户 {TenantId} 初始化完成", tenantId);
    }

    private async Task<Role> EnsureRolesAsync(CancellationToken cancellationToken)
    {
        var adminRole = await roleRepository.GetFirstAsync(r => r.Name == AdminConstant.RoleName, cancellationToken: cancellationToken);
        if (adminRole == null)
        {
            adminRole = new Role(
                name: AdminConstant.RoleName,
                displayName: "Administrator",
                description: "Tenant administrator with all tenant permissions",
                isStatic: true,
                isDefault: false,
                sort: 1
            );
            await roleRepository.InsertAsync(adminRole, cancellationToken);
        }

        if (!await roleRepository.AnyAsync(r => r.Name == MemberRoleName, cancellationToken))
        {
            await roleRepository.InsertAsync(new Role(
                name: MemberRoleName,
                displayName: "Member",
                description: "Default role automatically assigned to new users",
                isStatic: true,
                isDefault: true,
                sort: 100
            ), cancellationToken);
        }

        return adminRole;
    }

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

        // 只播种租户侧可见的定义：宿主侧权限（如 App.Tenants.*）授了也过不了检查器的侧别硬边界，
        // 且不应出现在租户的授予记录里
        var definitions = permissionDefinitionManager
            .GetAll()
            .Where(definition => definition.Side.HasFlag(MultiTenancySides.Tenant))
            .Where(definition => permissionDefinitionManager.IsEffectivelyEnabled(definition.Name))
            .Select(definition => definition.Name)
            .ToList();

        await permissionGrantManager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            providerKey,
            definitions,
            expectedRevision: existing.Revision,
            cancellationToken);

        logger.LogInformation("已为租户 {RoleName} 角色播种权限授予，共 {Count} 项", adminRole.Name, definitions.Count);
    }

    private async Task<User> EnsureTenantAdminAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken)
    {
        // 用户名租户内唯一：每个租户都有自己的 admin
        var adminUser = await userRepository.GetFirstAsync(u => u.Username == TenantAdminUsername, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = new User(
                username: TenantAdminUsername,
                email: adminEmail,
                passwordHash: passwordHasher.HashPassword(adminPassword),
                displayName: "Tenant Administrator"
            );

            // 租户内超管：旁路本租户功能权限，但不越过租户过滤器，也过不了宿主侧权限的侧别边界
            adminUser.MarkAsSuperAdmin();
            await userRepository.InsertAsync(adminUser, cancellationToken);
            logger.LogInformation("已创建租户管理员用户: {Username}", TenantAdminUsername);
        }

        return adminUser;
    }

    private async Task AssignAdminRoleAsync(User adminUser, Role adminRole, CancellationToken cancellationToken)
    {
        if (!await userRoleRepository.AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id, cancellationToken))
        {
            await userRoleRepository.InsertAsync(new UserRole(adminUser.Id, adminRole.Id), cancellationToken);
        }
    }
}
#endif
