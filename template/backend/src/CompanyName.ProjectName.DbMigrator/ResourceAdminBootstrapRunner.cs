using System.Security.Claims;
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using Leistd.AmbientContext;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Grants;
#if (IncludeMultiTenancy)
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
#endif
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Tenancy;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.DbMigrator;

/// <summary>资源管理员首次授予；部署权限是依据，不伪造自然人身份。</summary>
internal sealed class ResourceAdminBootstrapRunner(
    ICurrentTenant currentTenant,
    IAmbientContext ambientContext,
#if (IncludeMultiTenancy)
    IConnectionStringResolver connectionResolver,
    ITenantConnectionConfigurationStore tenantConnections,
#endif
    IUnitOfWorkManager unitOfWorkManager,
    IPermissionDefinitionManager definitions,
    IPermissionGrantStore grants,
    IPermissionGrantSeeder seeder,
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
        using var uow = unitOfWorkManager.Begin(requiresNew: true);
        var key = subject.ToString();
        var side = tenant.HasValue ? MultiTenancySides.Tenant : MultiTenancySides.Host;
        var existing = await grants.GetGrantsAsync(PermissionGrantProviderNames.User, key, cancellationToken);
        if (existing.Version != 0)
            return $"Subject {key} already has grant history (version {existing.Version}); nothing changed.";
        if (!apply)
        {
            var count = definitions.GetAll().Count(d => definitions.IsAvailableOn(d.Name, side));
            return $"Dry run: {count} {side} permissions would be granted to {key}; no data or audit record changed.";
        }
        // 角色与权限初始化同处此作用域。User 授予没有用户外键，允许首次投影前执行。
        await initializer.InitializeAsync(cancellationToken);
        var written = await seeder.SeedAllAsync(PermissionGrantProviderNames.User, key, side, cancellationToken);
        if (written is null) return $"Subject {key} was initialized concurrently; nothing changed.";
        await recorder.RecordSucceededAsync(OperationRecordActions.ResourceAdminGranted,
            OperationTarget.For($"User/{key}", key), OperationRecordAuthorizations.DeploymentBootstrap, cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return $"Granted {written} {side} permissions to {key}. Existing revocations will be preserved on subsequent runs.";
    }
}
