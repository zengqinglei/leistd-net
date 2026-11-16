#if (TenantWorkspace && OpenIddictServer)
using Leistd.Ddd.Application.Contracts.AppServices;
using Leistd.MultiTenancy.Stores;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public interface IWorkspaceTenantLookupAppService : IAppService
{
    Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantConfiguration?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceTenantLookupAppService(ITenantStore tenantStore, ITenantNormalizer normalizer) : IWorkspaceTenantLookupAppService
{
    public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default) => tenantStore.FindAsync(id, cancellationToken);
    public Task<TenantConfiguration?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => tenantStore.FindByNameAsync(normalizer.NormalizeName(name)!, cancellationToken);
}
#endif
