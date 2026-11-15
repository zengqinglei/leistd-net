#if (TenantWorkspace && RemoteTokenAuth)
using CompanyName.ProjectName.Domain.Tenants.Abstractions;
using Leistd.MultiTenancy.Stores;
using Leistd.ServiceClient.Http;

namespace CompanyName.ProjectName.Infrastructure.Workspaces;

public sealed class IdentityWorkspaceDirectory(IIdentityWorkspacesApi workspaces, IWorkspaceTenantApi tenants)
    : IIdentityWorkspaceDirectory
{
    public async Task<IReadOnlyList<IdentityWorkspace>> GetListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await workspaces.GetListAsync(cancellationToken);
        return (await response.ReadContentAsync<List<IdentityWorkspace>>(cancellationToken: cancellationToken))!;
    }

    public async Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        using var response = await tenants.FindByNameAsync(normalizedName, cancellationToken);
        return await ReadTenantAsync(response, cancellationToken);
    }

    private static async Task<TenantConfiguration?> ReadTenantAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await response.ReadContentAsync<TenantConfiguration>(cancellationToken: cancellationToken);
    }
}
#endif
