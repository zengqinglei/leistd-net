#if (IdentityService)
using System.ComponentModel.DataAnnotations;
using Leistd.MultiTenancy;

namespace CompanyName.ProjectName.Application.TenantConnections.Dtos;

public sealed record TenantConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record TenantRuntimeConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record TenantMigrationConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record UpdateTenantConnectionInputDto
{
    public required TenantDatabaseMode DatabaseMode { get; init; }

    [MaxLength(512)]
    public string? RuntimeSecretReference { get; init; }

    [MaxLength(512)]
    public string? MigrationSecretReference { get; init; }
}
#endif
