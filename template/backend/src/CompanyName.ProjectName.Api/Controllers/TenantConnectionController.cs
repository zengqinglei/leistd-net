#if (LocalIdentity)
using CompanyName.ProjectName.Application.Permissions.Provider;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
#endif
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

#if (OpenIddictServer)
    /// <remarks>
    /// 机器端点：只接受 client credentials 令牌（见 <c>AddApiAuthorization</c>）。
    /// 只在签发令牌的形态下生成——不签发令牌时不可能存在机器主体，
    /// 生成一个永远无人可用的内部端点只是攻击面。
    /// </remarks>
    [HttpGet("runtime/{tenantId:guid}")]
    [Authorize(Policy = TenantConnectionPolicies.RuntimeRead)]
    public Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        service.GetRuntimeAsync(tenantId, cancellationToken);

    /// <inheritdoc cref="GetRuntimeAsync"/>
    [HttpGet("migration")]
    [Authorize(Policy = TenantConnectionPolicies.MigrationRead)]
    public Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        CancellationToken cancellationToken) =>
        service.GetMigrationListAsync(cancellationToken);
#endif

    [HttpPut("{tenantId:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantConnectionOutputDto> UpdateAsync(
        Guid tenantId,
        [FromBody] UpdateTenantConnectionInputDto input,
        CancellationToken cancellationToken) =>
        service.UpdateAsync(tenantId, input, cancellationToken);
}
#endif
