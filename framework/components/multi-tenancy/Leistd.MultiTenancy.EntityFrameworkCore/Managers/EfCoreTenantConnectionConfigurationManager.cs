using Leistd.UnitOfWork.EfCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary><see cref="ITenantConnectionConfigurationManager"/> 的 EF Core 实现。</summary>
public class EfCoreTenantConnectionConfigurationManager<TDbContext>(IDbContextProvider<TDbContext> dbContextProvider)
    : ITenantConnectionConfigurationManager
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<TenantConnectionConfiguration> SetAsync(
        Guid tenantId,
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference,
        CancellationToken cancellationToken = default)
    {
        Validate(databaseMode, runtimeSecretReference, migrationSecretReference);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        var tenantExists = await dbContext.Set<TenantRecord>()
            .AnyAsync(x => x.Id == tenantId && !x.IsDeleted, cancellationToken);
        if (!tenantExists)
        {
            throw new TenantNotFoundException(tenantId.ToString());
        }

        var record = await dbContext.Set<TenantConnectionRecord>()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (record is null)
        {
            record = new TenantConnectionRecord
            {
                TenantId = tenantId,
                Version = 1
            };
            dbContext.Set<TenantConnectionRecord>().Add(record);
        }
        else
        {
            record.Version++;
        }

        record.DatabaseMode = databaseMode;
        record.RuntimeSecretReference = Normalize(runtimeSecretReference);
        record.MigrationSecretReference = Normalize(migrationSecretReference);

        await dbContext.SaveChangesAsync(cancellationToken);
        return EfCoreTenantConnectionConfigurationStore<TDbContext>.ToConfiguration(record);
    }

    private static void Validate(
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference)
    {
        switch (databaseMode)
        {
            case TenantDatabaseMode.SharedDatabase when
                runtimeSecretReference is not null || migrationSecretReference is not null:
                throw new ArgumentException("Shared database configuration cannot contain Secret references.");

            case TenantDatabaseMode.DedicatedDatabase when
                string.IsNullOrWhiteSpace(runtimeSecretReference) ||
                string.IsNullOrWhiteSpace(migrationSecretReference):
                throw new ArgumentException("Dedicated database configuration requires runtime and migration Secret references.");

            case TenantDatabaseMode.SharedDatabase:
            case TenantDatabaseMode.DedicatedDatabase:
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(databaseMode));
        }
    }

    private static string? Normalize(string? value) => value?.Trim();
}
