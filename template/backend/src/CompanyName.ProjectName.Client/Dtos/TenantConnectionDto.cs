#if (LocalIdentity)
using Leistd.MultiTenancy.ConnectionStrings;
namespace CompanyName.ProjectName.Client.Dtos;

public enum TenantDatabaseMode
{
    SharedDatabase,
    DedicatedDatabase
}

public sealed record TenantRuntimeConnectionDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record TenantMigrationConnectionDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}
#endif
