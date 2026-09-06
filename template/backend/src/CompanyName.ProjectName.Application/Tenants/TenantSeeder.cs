using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Logging;
using Leistd.Authorization.Constants;
using Leistd.Lock;
using Leistd.Authorization.Abstractions;
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Abstractions;

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
    UserDomainService userDomainService,
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
            throw new InvalidOperationException("Tenant seeding must run inside a tenant context: call ICurrentTenant.Change(tenantId) first.");
        }

        // 按租户互斥，避免不同租户相互阻塞。
        await using var lockHandle = await distributedLock.LockAsync($"MyProject:tenant-init:{tenantId}", cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lockHandle.LockLost);
        cancellationToken = lockScope.Token;

        logger.LogInformation("Initializing tenant {TenantId}...", tenantId);

        var adminRole = await EnsureRolesAsync(cancellationToken);
        await SeedAdminRolePermissionsAsync(adminRole, cancellationToken);
        var adminUser = await EnsureTenantAdminAsync(adminEmail, adminPassword, cancellationToken);
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);

        logger.LogInformation("Tenant {TenantId} initialized", tenantId);
    }

    /// <inheritdoc />
    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        if (currentTenant.Id is not { } tenantId)
        {
            throw new InvalidOperationException("Tenant data purge must run inside a tenant context: call ICurrentTenant.Change(tenantId) first.");
        }

        // 租户过滤器限定查询和写入；先物化以支持后续多次枚举。
        var users = (await userRepository.GetListAsync(cancellationToken: cancellationToken)).ToList();
        var roles = (await roleRepository.GetListAsync(cancellationToken: cancellationToken)).ToList();

        // 永久废弃主体时硬删授予和授权版本，避免孤儿行。
        foreach (var role in roles)
        {
            await permissionGrantManager.RemoveProviderAsync(
                PermissionGrantProviderNames.Role, role.Id.ToString(), cancellationToken);
        }

        foreach (var user in users)
        {
            await permissionGrantManager.RemoveProviderAsync(
                PermissionGrantProviderNames.User, user.Id.ToString(), cancellationToken);
        }

        // 主体和关联均软删除；必须先删主体再加载关联。
        // 关联先进入 Modified 状态会使 EF 在删除必需关系主体时立即报错。
        if (users.Count > 0)
        {
            await userRepository.DeleteManyAsync(users, cancellationToken);
        }

        if (roles.Count > 0)
        {
            await roleRepository.DeleteManyAsync(roles, cancellationToken);
        }

        if (users.Count > 0)
        {
            var userIds = users.Select(u => u.Id).ToList();
            await userRoleRepository.DeleteManyAsync(ur => userIds.Contains(ur.UserId), cancellationToken);
        }

        logger.LogInformation(
            "Purged seed data for tenant {TenantId}: {UserCount} user(s), {RoleCount} role(s)",
            tenantId, users.Count, roles.Count);
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

        if (existing.Version != 0)
        {
            return;
        }

        // 只播种租户侧可见的权限定义。
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
            expectedVersion: existing.Version,
            cancellationToken);

        logger.LogInformation("Seeded permission grants for tenant role {RoleName}: {Count} item(s)", adminRole.Name, definitions.Count);
    }

    /// <summary>
    /// 建立租户管理员：<b>普通用户</b> + 本租户 Admin 角色
    /// </summary>
    /// <remarks>
    /// <para><b>刻意不打 <c>IsSuperAdmin</c>。</b>那是宿主的防锁死逃生舱，代价与适用范围见
    /// <c>UserDomainService.CreateSuperAdminAsync</c>。租户侧不需要它：本租户 Admin 角色已在
    /// 上一步拿到全部 Tenant 侧权限（含权限管理本身），升级后新增的权限由租户管理员自己授给
    /// 自己的角色，不存在锁死。</para>
    /// <para>于是租户管理员的权限完全由角色承载：在租户自己的角色页里可查看、可编辑、可撤销，
    /// 撤权即时生效。</para>
    /// </remarks>
    private async Task<User> EnsureTenantAdminAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken)
    {
        var adminUser = await userRepository.GetFirstAsync(u => u.Username == TenantAdminUsername, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = await userDomainService.CreateUserAsync(
                TenantAdminUsername,
                adminEmail,
                adminPassword,
                displayName: "Tenant Administrator",
                passwordSubject: "Tenant admin password",
                cancellationToken);
            logger.LogInformation("Tenant admin user created: {Username}", TenantAdminUsername);
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
