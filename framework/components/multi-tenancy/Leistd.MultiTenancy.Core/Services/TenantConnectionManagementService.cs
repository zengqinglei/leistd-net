using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.Services;

internal sealed class TenantConnectionManagementService(
    ITenantConnectionDirectory directory,
    ITenantConnectionConfigurationStore store,
    ITenantConnectionConfigurationManager manager) : ITenantConnectionManagementService
{
    public async Task<IReadOnlyList<TenantConnectionOutputDto>> GetListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entries = await directory.ListAsync(tenantId, cancellationToken)
            ?? throw new TenantNotFoundException(tenantId.ToString());

        return [.. entries.Select(entry => new TenantConnectionOutputDto { TenantId = tenantId, Name = entry.Name, Version = entry.Version })];
    }

    public async Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var lookup = await store.FindAsync(tenantId, TenantConnectionNames.NormalizeInput(name), cancellationToken)
            ?? throw new TenantNotFoundException(tenantId.ToString());

        return new TenantRuntimeConnectionOutputDto
        {
            TenantId = lookup.TenantId,
            HasAnyConnection = lookup.HasAnyConnection,
            Connection = lookup.Connection is { } connection
                ? new TenantConnectionDetailOutputDto
                {
                    Name = connection.Name,
                    ConnectionString = connection.ConnectionString,
                    Version = connection.Version
                }
                : null
        };
    }

    public async Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var connections = await store.GetListAsync(TenantConnectionNames.NormalizeInput(name), cancellationToken);
        return [.. connections.Select(connection => new TenantMigrationConnectionOutputDto
        {
            TenantId = connection.TenantId,
            Name = connection.Name,
            ConnectionString = connection.ConnectionString
        })];
    }

    public async Task<TenantConnectionOutputDto> SetAsync(
        Guid tenantId,
        string name,
        UpsertTenantConnectionInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var configuration = await manager.SetAsync(tenantId, name, input.ConnectionString, input.ExpectedVersion, cancellationToken);
        return new TenantConnectionOutputDto
        {
            TenantId = configuration.TenantId,
            Name = configuration.Name,
            Version = configuration.Version
        };
    }

    public Task RemoveAsync(Guid tenantId, string name, long expectedVersion, CancellationToken cancellationToken = default)
        => manager.RemoveAsync(tenantId, name, expectedVersion, cancellationToken);
}
