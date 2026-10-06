using System.Security.Claims;
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using Leistd.AmbientContext;
using Leistd.Auditing.Abstractions;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Grants;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
#if (IncludeMultiTenancy)
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
#endif
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Tenancy;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.DbMigrator;

/// <summary>资源服务首位管理员引导：把远端主体加入 Administrator 角色；部署权限是依据，不伪造自然人身份。</summary>
/// <remarks>
/// <para>授权载体是角色，不是给这个人的直接授予：首位管理员与之后的管理员一样由角色管理，
/// 调整 Administrator 的权限、移除其成员关系都能收回。直接授予在本服务没有管理入口，收不回。</para>
/// <para>本地还没有这个主体（远端用户尚未访问过）时，先建只含主体标识的最小用户行，
/// 首次真实访问时按令牌补齐资料。</para>
/// <para>只引导一次：这个人曾被移出 Administrator（成员关系的软删除历史）就不再加回，
/// 重跑命令不会推翻之后的撤销；要恢复走正式角色管理。</para>
/// </remarks>
internal sealed class ResourceAdminBootstrapRunner(
    ICurrentTenant currentTenant,
    IAmbientContext ambientContext,
#if (IncludeMultiTenancy)
    IConnectionStringResolver connectionResolver,
    ITenantConnectionConfigurationStore tenantConnections,
#endif
    IUnitOfWorkManager unitOfWorkManager,
    IDistributedLock distributedLock,
    IDataFilter dataFilter,
    IRepository<Role, Guid> roleRepository,
    IRepository<User, Guid> userRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    UserDomainService userDomainService,
    IPermissionDefinitionManager definitions,
    IPermissionGrantStore grants,
    ISystemInitializer initializer,
    IOperationRecorder recorder)
{
    public async Task<string> RunAsync(Guid subject, Guid? tenant, bool apply, CancellationToken cancellationToken = default)
    {
#if (IncludeMultiTenancy)
        if (tenant is { } tenantId)
        {
            var lookup = await tenantConnections.FindAsync(tenantId, ConnectionStringNames.Default, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant '{tenantId}' does not exist.");
            if (lookup.TenantId != tenantId)
                throw new InvalidOperationException("The control plane returned a different tenant.");
        }
#else
        if (tenant.HasValue) throw new InvalidOperationException("This application only accepts the host scope.");
#endif
        using var tenantScope = currentTenant.Change(tenant);
#if (IncludeMultiTenancy)
        // 先解析实际落点；不会打印含凭据的连接串。缺失路由与回源错误必须失败。
        _ = await connectionResolver.ResolveAsync(ConnectionStringNames.Default, cancellationToken);
#endif
        using var actorScope = ambientContext.Begin(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", $"deployment:{Environment.UserName}@{Environment.MachineName}")], "deployment")),
            correlationId: Guid.NewGuid().ToString("N"));

        // 首次初始化与授权写在本事务里，锁要持有到提交之后：
        // 提交前放锁，另一入口拿到锁时读不到未提交的角色，会再建一个。
        await using var lockHandle = await distributedLock.LockAsync(SystemInitializer.InitializationLockKey, cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lockHandle.LockLost);
        cancellationToken = lockScope.Token;

        using var uow = unitOfWorkManager.Begin(requiresNew: true);
        var key = subject.ToString();
        var side = tenant.HasValue ? MultiTenancySides.Tenant : MultiTenancySides.Host;
        var adminRole = await FindAdminRoleAsync(cancellationToken);
        User? user;
        using (dataFilter.Disable<ISoftDelete>())
        {
            user = await userRepository.GetByIdAsync(subject, cancellationToken);
        }

        if (user is { IsDeleted: true })
            throw new InvalidOperationException($"User {key} has been deleted in this service and cannot be bootstrapped; recover it through your data recovery procedure first.");

        if (adminRole is not null && user is not null)
        {
            List<UserRole> memberships;
            // 软删除的成员关系就是"曾被移出"的历史；租户过滤保持开启
            using (dataFilter.Disable<ISoftDelete>())
            {
                memberships = [.. await userRoleRepository.GetListAsync(
                    ur => ur.UserId == subject && ur.RoleId == adminRole.Id, cancellationToken)];
            }

            if (memberships.Any(ur => !ur.IsDeleted))
                return $"User {key} already holds the {AdminConstant.RoleName} role; nothing changed.";
            if (memberships.Count != 0)
                return $"User {key} was removed from the {AdminConstant.RoleName} role earlier; re-assign it through role management. Nothing changed.";
        }

        if (!apply)
        {
            var roleStep = adminRole is null
                ? $"create the {AdminConstant.RoleName} role and seed {AvailablePermissionCount(side)} {side} permissions"
                : await DescribeExistingRoleAsync(adminRole, side, cancellationToken);
            var userStep = user is null
                ? "create a minimal local user"
                : $"reuse local user '{user.Username}' ({(user.IsActive ? "active" : "inactive")}) without changing it";
            return $"Dry run for {key}: would {roleStep}, {userStep}, and assign the {AdminConstant.RoleName} role. "
                + "No data or audit record changed.";
        }

        adminRole = await initializer.InitializeWithinLockAsync(cancellationToken);

        var (_, created) = await userDomainService.EnsureSubjectAsync(subject, cancellationToken);
        await userRoleRepository.InsertAsync(new UserRole(subject, adminRole.Id), cancellationToken);
        await recorder.RecordSucceededAsync(OperationRecordActions.ResourceAdminGranted,
            OperationTarget.For($"User/{key}", key), OperationRecordAuthorizations.DeploymentBootstrap, cancellationToken);
        await uow.CompleteAsync(cancellationToken);

        return $"Assigned the {AdminConstant.RoleName} role to {key}"
            + (created ? " (created a minimal local user; its profile fills in on first sign-in)" : string.Empty)
            + ". Later role changes are managed through role management; re-running will not undo them.";
    }

    private Task<Role?> FindAdminRoleAsync(CancellationToken cancellationToken)
        => roleRepository.GetFirstAsync(r => r.Name == AdminConstant.RoleName, cancellationToken: cancellationToken);

    private int AvailablePermissionCount(MultiTenancySides side)
        => definitions.GetAll().Count(d => definitions.IsAvailableOn(d.Name, side));

    // 角色已经播种过就按现状如实说，不能在权限被人为缩减后仍报"将授予全部"
    private async Task<string> DescribeExistingRoleAsync(Role adminRole, MultiTenancySides side, CancellationToken cancellationToken)
    {
        var current = await grants.GetGrantsAsync(PermissionGrantProviderNames.Role, adminRole.Id.ToString(), cancellationToken);
        return current.Version == 0
            ? $"seed {AvailablePermissionCount(side)} {side} permissions to the existing {AdminConstant.RoleName} role"
            : $"keep the {AdminConstant.RoleName} role's current {current.PermissionNames.Count} permissions";
    }
}
