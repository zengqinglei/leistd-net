using Leistd.UnitOfWork.EfCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>直接读取 Identity Control DB 的租户连接配置存储。</summary>
public class EfCoreTenantConnectionConfigurationStore<TDbContext>(IDbContextProvider<TDbContext> dbContextProvider)
    : ITenantConnectionConfigurationStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<TenantConnectionConfiguration?> FindAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var record = await dbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        return record is null ? null : ToConfiguration(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantConnectionConfiguration>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var records = await (
                from connection in dbContext.Set<TenantConnectionRecord>().AsNoTracking()
                join tenant in dbContext.Set<TenantRecord>().AsNoTracking()
                    on connection.TenantId equals tenant.Id
                where !tenant.IsDeleted
                orderby connection.TenantId
                select connection)
            .ToListAsync(cancellationToken);

        return records.Select(ToConfiguration).ToList();
    }

    internal static TenantConnectionConfiguration ToConfiguration(TenantConnectionRecord record) => new()
    {
        TenantId = record.TenantId,
        DatabaseMode = record.DatabaseMode,
        RuntimeSecretReference = record.RuntimeSecretReference,
        MigrationSecretReference = record.MigrationSecretReference,
        Version = record.Version
    };
}
