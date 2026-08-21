#if (IdentityService)
using CompanyName.ProjectName.Application.TenantConnections.Dtos;

namespace CompanyName.ProjectName.Application.TenantConnections.AppServices;

public interface ITenantConnectionAppService
{
    Task<TenantConnectionOutputDto> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        CancellationToken cancellationToken = default);

    Task<TenantConnectionOutputDto> UpdateAsync(
        Guid tenantId,
        UpdateTenantConnectionInputDto input,
        CancellationToken cancellationToken = default);
}
#endif
