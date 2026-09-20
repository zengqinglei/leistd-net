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
using Leistd.Settings.Abstractions;
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.Provisioning;
using CompanyName.ProjectName.Application.Tenants.Dtos;

namespace CompanyName.ProjectName.Application.Tenants;

/// <summary>
/// 租户开通：在新租户里建初始角色、权限授予与租户管理员；创建失败时清掉写过的东西。
/// </summary>
/// <remarks>
/// <para>多租户组件的租户管理用例在目标租户上下文与新工作单元里调用它（见 <see cref="ITenantProvisioner"/>），
/// 所有仓储与授予读写都自动分区到该租户；分库租户的写入直接落进它的专属库。</para>
/// <para>与宿主侧 <c>SystemInitializer</c> 的判据一致：角色"先查后建"，权限经首次授予写入
/// （从未写过才写，涵盖首建与上次中断，不会把人工撤权补回来）。
/// 互斥锁按租户隔离（锁 key 含 tenantId），不同租户的开通互不排队。</para>
/// </remarks>
public class TenantSeeder(
    ICurrentTenant currentTenant,
    IRepository<User, Guid> userRepository,
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    UserDomainService userDomainService,
    IPermissionGrantSeeder permissionGrantSeeder,
    IPermissionGrantManager permissionGrantManager,
    IDistributedLock distributedLock,
    ISettingStore settingStore,
    ILogger<TenantSeeder> logger) : ITenantProvisioner
{
    private const string MemberRoleName = "Member";

    /// <inheritdoc />
    public async Task ProvisionAsync(TenantProvisioningContext context, CancellationToken cancellationToken = default)
    {
        // 端点按 CreateTenantWithAdminInputDto 绑定请求体；拿到基类说明宿主把端点映射错了类型
        var input = context.Input as CreateTenantWithAdminInputDto
            ?? throw new InvalidOperationException(
                $"Tenant provisioning needs a {nameof(CreateTenantWithAdminInputDto)}; map the tenant endpoints with that request type.");

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
        var adminUser = await EnsureTenantAdminAsync(input.AdminEmail, input.AdminPassword, cancellationToken);
        await AssignAdminRoleAsync(adminUser, adminRole, cancellationToken);

        logger.LogInformation("Tenant {TenantId} initialized", tenantId);
    }

    /// <inheritdoc />
    public async Task PurgeAsync(TenantProvisioningContext context, CancellationToken cancellationToken = default)
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

        // 设置行带租户归属：租户永久废弃后它们既读不到也删不掉，必须一并清理。
        await settingStore.RemoveAllAsync(cancellationToken);

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

    // 只授予租户侧可用的权限；从未写过授予才写，之后按普通角色管理
    private async Task SeedAdminRolePermissionsAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var granted = await permissionGrantSeeder.SeedAllAsync(
            PermissionGrantProviderNames.Role,
            adminRole.Id.ToString(),
            MultiTenancySides.Tenant,
            cancellationToken);

        if (granted is { } count)
        {
            logger.LogInformation("Seeded permission grants for tenant role {RoleName}: {Count} item(s)", adminRole.Name, count);
        }
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
        var adminUser = await userRepository.GetFirstAsync(u => u.Username == AdminConstant.TenantAdminUsername, q => q.OrderBy(u => u.Id), cancellationToken);
        if (adminUser == null)
        {
            adminUser = await userDomainService.CreateUserAsync(
                AdminConstant.TenantAdminUsername,
                adminEmail,
                adminPassword,
                displayName: "Tenant Administrator",
                passwordSubject: "Tenant admin password",
                cancellationToken);
            logger.LogInformation("Tenant admin user created: {Username}", AdminConstant.TenantAdminUsername);
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
