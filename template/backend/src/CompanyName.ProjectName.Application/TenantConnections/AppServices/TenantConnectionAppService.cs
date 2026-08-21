#if (IdentityService)
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Leistd.Exception.Core;
using Leistd.MultiTenancy;

namespace CompanyName.ProjectName.Application.TenantConnections.AppServices;

public sealed class TenantConnectionAppService(
    ITenantConnectionConfigurationStore store,
    ITenantConnectionConfigurationManager manager) : ITenantConnectionAppService
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
        return new TenantRuntimeConnectionOutputDto
        {
            TenantId = configuration.TenantId,
            DatabaseMode = configuration.DatabaseMode,
            RuntimeSecretReference = configuration.RuntimeSecretReference,
            Version = configuration.Version
        };
    }

    public async Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        CancellationToken cancellationToken = default)
    {
        var configurations = await store.GetListAsync(cancellationToken);
        return configurations.Select(configuration => new TenantMigrationConnectionOutputDto
        {
            TenantId = configuration.TenantId,
            DatabaseMode = configuration.DatabaseMode,
            MigrationSecretReference = configuration.MigrationSecretReference,
            Version = configuration.Version
        }).ToList();
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
            cancellationToken);
        return ToOutput(configuration);
    }

    private static TenantConnectionOutputDto ToOutput(TenantConnectionConfiguration configuration) => new()
    {
        TenantId = configuration.TenantId,
        DatabaseMode = configuration.DatabaseMode,
        RuntimeSecretReference = configuration.RuntimeSecretReference,
        MigrationSecretReference = configuration.MigrationSecretReference,
        Version = configuration.Version
    };
}
#endif
