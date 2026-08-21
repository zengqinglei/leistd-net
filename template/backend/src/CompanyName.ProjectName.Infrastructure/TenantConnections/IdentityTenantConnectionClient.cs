#if (ResourceService)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal enum RemoteTenantDatabaseMode
{
    SharedDatabase,
    DedicatedDatabase
}

internal sealed record RemoteTenantRuntimeConnectionConfiguration
{
    public required Guid TenantId { get; init; }
    public required RemoteTenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public required long Version { get; init; }
}

internal sealed record RemoteTenantMigrationConnectionConfiguration
{
    public required Guid TenantId { get; init; }
    public required RemoteTenantDatabaseMode DatabaseMode { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

internal interface IIdentityTenantConnectionClient
{
    [Get("/api/v1/tenant-connections/runtime/{tenantId}")]
    Task<RemoteTenantRuntimeConnectionConfiguration> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    [Get("/api/v1/tenant-connections/migration")]
    Task<IReadOnlyList<RemoteTenantMigrationConnectionConfiguration>> GetMigrationListAsync(
        CancellationToken cancellationToken = default);
}
#endif
