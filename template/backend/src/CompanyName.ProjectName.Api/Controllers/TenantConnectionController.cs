#if (IdentityService)
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.TenantConnections.AppServices;
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

[Authorize]
[Route("api/v1/tenant-connections")]
public sealed class TenantConnectionController(ITenantConnectionAppService service) : BaseController
{
    [HttpGet("{tenantId:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantConnectionOutputDto> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        service.GetAsync(tenantId, cancellationToken);

    [HttpGet("runtime/{tenantId:guid}")]
    [Authorize(Policy = "TenantConnection.RuntimeRead")]
    public Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        service.GetRuntimeAsync(tenantId, cancellationToken);

    [HttpGet("migration")]
    [Authorize(Policy = "TenantConnection.MigrationRead")]
    public Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        CancellationToken cancellationToken) =>
        service.GetMigrationListAsync(cancellationToken);

    [HttpPut("{tenantId:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantConnectionOutputDto> UpdateAsync(
        Guid tenantId,
        [FromBody] UpdateTenantConnectionInputDto input,
        CancellationToken cancellationToken) =>
        service.UpdateAsync(tenantId, input, cancellationToken);
}
#endif
