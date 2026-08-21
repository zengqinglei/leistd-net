using CompanyName.ProjectName.Infrastructure.Persistence;
using CompanyName.ProjectName.Infrastructure.TenantConnections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
#if (IdentityService)
using OpenIddict.EntityFrameworkCore;
#endif

namespace CompanyName.ProjectName.DbMigrator;

public sealed class DatabaseMigrationRunner(
    IConfiguration configuration,
    ITenantMigrationTargetProvider targetProvider,
    ILogger<DatabaseMigrationRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var explicitTarget = configuration.GetConnectionString("MigrationTarget");
        if (!string.IsNullOrWhiteSpace(explicitTarget))
        {
            var target = new TenantMigrationTarget(Guid.Empty, explicitTarget);
            await MigrateBusinessAsync(explicitTarget, target.Fingerprint, cancellationToken);
            return;
        }

        var defaultConnection = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(defaultConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is required by DbMigrator.");
        }

#if (IdentityService)
        await MigrateControlAsync(defaultConnection, cancellationToken);
#endif
        await MigrateBusinessAsync(defaultConnection, "default", cancellationToken);

        var targets = await targetProvider.GetDedicatedTargetsAsync(cancellationToken);
        foreach (var target in targets.DistinctBy(x => x.Fingerprint, StringComparer.Ordinal))
        {
            await MigrateBusinessAsync(target.ConnectionString, target.Fingerprint, cancellationToken);
        }
    }

#if (IdentityService)
    private async Task MigrateControlAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Migrating the Identity control database.");
        var options = new DbContextOptionsBuilder<IdentityControlDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Control", "companyname-projectname"))
            .UseOpenIddict();

        await using var dbContext = new IdentityControlDbContext(options.Options);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
#endif

    private async Task MigrateBusinessAsync(
        string connectionString,
        string targetFingerprint,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Migrating database target {DatabaseTarget}.", targetFingerprint);

        var options = new DbContextOptionsBuilder<MyProjectDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "companyname-projectname"));
        await using var dbContext = new MyProjectDbContext(options.Options, serviceProvider: null);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
