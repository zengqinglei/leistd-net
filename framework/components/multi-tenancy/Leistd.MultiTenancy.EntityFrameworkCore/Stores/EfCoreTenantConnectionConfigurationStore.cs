using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Stores;

/// <summary>直接读取 Identity Control DB 的租户连接配置存储。</summary>
/// <remarks>
/// 两个读方法都经 <see cref="TenantQueryableExtensions.ConnectionsOfUndeletedTenants"/>
/// 起查：已删除租户的连接配置行仍在库里，控制面上下文又没有软删除过滤器兜底，
/// 少这道 join 就会把路由交给一个已经被删掉的租户。
/// </remarks>
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
        var record = await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        return record is null ? null : ToConfiguration(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantConnectionConfiguration>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var records = await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .OrderBy(x => x.TenantId)
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
