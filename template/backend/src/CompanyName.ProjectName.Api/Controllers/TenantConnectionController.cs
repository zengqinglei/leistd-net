#if (LocalIdentity)
using CompanyName.ProjectName.Application.Permissions.Provider;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
using CompanyName.ProjectName.Application.TenantConnections.Constants;
#endif
using CompanyName.ProjectName.Application.TenantConnections.AppServices;
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <remarks>
/// 连接名走路由段：一个租户在 identity、foundation、crm 各可以有一条，名字就是使用方
/// DbContext 的 <c>[ConnectionStringName]</c>。名字大小写不敏感，由框架归一化。
/// </remarks>
[Authorize]
[Route("api/v1/tenant-connections")]
public sealed class TenantConnectionController(ITenantConnectionAppService service) : BaseController
{
    /// <summary>列出该租户已登记的连接（不含连接串）。空列表即该租户不单独分库。</summary>
    [HttpGet("{tenantId:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<IReadOnlyList<TenantConnectionOutputDto>> GetListAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        service.GetListAsync(tenantId, cancellationToken);

#if (OpenIddictServer)
    /// <remarks>
    /// 机器端点：只接受 client credentials 令牌（见 <c>AddApiAuthorization</c>）。
    /// 只在签发令牌的形态下生成——不签发令牌时不可能存在机器主体，
    /// 生成一个永远无人可用的内部端点只是攻击面。
    /// <b>按名字问、按名字答</b>：一次只回被问到的那一条，不把该租户在别的服务的连接串也发出去。
    /// </remarks>
    [HttpGet("runtime/{tenantId:guid}")]
    [Authorize(Policy = TenantConnectionPolicies.RuntimeRead)]
    public Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        [FromQuery] string name,
        CancellationToken cancellationToken) =>
        service.GetRuntimeAsync(tenantId, name, cancellationToken);

    /// <inheritdoc cref="GetRuntimeAsync"/>
    [HttpGet("migration")]
    [Authorize(Policy = TenantConnectionPolicies.MigrationRead)]
    public Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        [FromQuery] string name,
        CancellationToken cancellationToken) =>
        service.GetMigrationListAsync(name, cancellationToken);
#endif

    /// <summary>登记或更新一条连接。</summary>
    [HttpPut("{tenantId:guid}/{name}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantConnectionOutputDto> SetAsync(
        Guid tenantId,
        string name,
        [FromBody] UpsertTenantConnectionInputDto input,
        CancellationToken cancellationToken) =>
        service.SetAsync(tenantId, name, input, cancellationToken);

    /// <summary>删除一条连接，该名字之后回落到服务自己的配置。</summary>
    [HttpDelete("{tenantId:guid}/{name}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task RemoveAsync(
        Guid tenantId,
        string name,
        [FromQuery] long expectedVersion,
        CancellationToken cancellationToken) =>
        service.RemoveAsync(tenantId, name, expectedVersion, cancellationToken);
}
#endif
