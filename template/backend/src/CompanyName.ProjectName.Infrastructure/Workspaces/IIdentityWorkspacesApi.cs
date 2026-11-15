#if (TenantWorkspace && RemoteTokenAuth)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.Workspaces;

public interface IIdentityWorkspacesApi
{
    [Get("/api/v1/auth/me/workspaces")]
    Task<HttpResponseMessage> GetListAsync(CancellationToken cancellationToken = default);
}

public interface IWorkspaceTenantApi
{
    [Get("/api/v1/workspace-tenants/{id}")]
    Task<HttpResponseMessage> FindAsync(Guid id, CancellationToken cancellationToken = default);

    [Get("/api/v1/workspace-tenants/by-name/{name}")]
    Task<HttpResponseMessage> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
#endif
