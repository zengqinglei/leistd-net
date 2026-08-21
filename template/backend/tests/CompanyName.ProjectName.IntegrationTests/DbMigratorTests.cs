using CompanyName.ProjectName.Infrastructure.TenantConnections;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class DbMigratorTests
{
    [Fact]
    public void Targets_with_the_same_physical_connection_are_migrated_once()
    {
        var connection = "Host=database;Database=shared;Username=ddl;Password=secret";
        var targets = new[]
        {
            new TenantMigrationTarget(Guid.NewGuid(), connection),
            new TenantMigrationTarget(Guid.NewGuid(), connection)
        };

        Assert.Single(targets.DistinctBy(x => x.Fingerprint, StringComparer.Ordinal));
        Assert.DoesNotContain("Password", targets[0].Fingerprint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", targets[0].Fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
