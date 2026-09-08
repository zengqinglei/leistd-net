#if (LocalIdentity)
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Leistd.MultiTenancy;
using Leistd.ObjectMapping.Abstractions;
using Leistd.Ddd.Application.AppService;

namespace CompanyName.ProjectName.Application.TenantConnections.AppServices;

public sealed class TenantConnectionAppService(
    ITenantConnectionConfigurationStore store,
    ITenantConnectionConfigurationManager manager,
    IObjectMapper objectMapper) : BaseAppService, ITenantConnectionAppService
{
    public async Task<TenantConnectionOutputDto> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await store.FindAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant connection configuration was not found.");
        return ToOutput(configuration);
    }

    public async Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await store.FindAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant connection configuration was not found.");
        return objectMapper.Map<TenantConnectionConfiguration, TenantRuntimeConnectionOutputDto>(configuration);
    }

    public async Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        CancellationToken cancellationToken = default)
    {
        var configurations = await store.GetListAsync(cancellationToken);
        return [.. configurations.Select(objectMapper.Map<TenantConnectionConfiguration, TenantMigrationConnectionOutputDto>)];
    }

    public async Task<TenantConnectionOutputDto> UpdateAsync(
        Guid tenantId,
        UpdateTenantConnectionInputDto input,
        CancellationToken cancellationToken = default)
    {
        var configuration = await manager.SetAsync(
            tenantId,
            input.DatabaseMode,
            input.RuntimeSecretReference,
            input.MigrationSecretReference,
            input.ExpectedVersion,
            cancellationToken);
        return ToOutput(configuration);
    }

    private TenantConnectionOutputDto ToOutput(TenantConnectionConfiguration configuration)
        => objectMapper.Map<TenantConnectionConfiguration, TenantConnectionOutputDto>(configuration);
}
#endif
