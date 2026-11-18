#if (TenantWorkspace && OpenIddictServer)
using CompanyName.ProjectName.Api.Auth.Authorization;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using Leistd.MultiTenancy.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

[Authorize(Policy = ApiPolicies.IdentityMachine)]
[Route("api/v1/workspace-tenants")]
public sealed class WorkspaceTenantController(IWorkspaceTenantLookupAppService lookup) : BaseController
{
    [HttpGet("{id:guid}")]
    public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken) => lookup.FindAsync(id, cancellationToken);

    [HttpGet("by-name/{name}")]
    public Task<TenantConfiguration?> FindByNameAsync(string name, CancellationToken cancellationToken) => lookup.FindByNameAsync(name, cancellationToken);
}
#endif
