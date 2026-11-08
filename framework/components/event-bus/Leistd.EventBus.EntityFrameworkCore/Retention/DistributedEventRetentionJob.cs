using System.Reflection;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Options;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Retention;

internal sealed class DistributedEventRetentionJob<TDbContext>(
    ITenantDatabaseRunner databases,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    IOptionsSnapshot<DistributedEventRetentionOptions> options,
    TimeProvider clock) : IRecurringJob where TDbContext : DbContext
{
    internal const string Name = "event-bus.retention";
    private static readonly string ConnectionName = typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.Days);
        var result = await databases.ForEachDatabaseAsync(ConnectionName, activeOnly: false, async (database, ct) =>
        {
            // Pending 永不删除；恢复会更新 LastModificationTime，重新开始保留期。
            await DeleteAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters()
                .Where(x => x.Status != DistributedEventOutboxStatus.Pending && x.LastModificationTime < cutoff).OrderBy(x => x.Id), ct);
            await DeleteAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters()
                .Where(x => x.Status != DistributedEventInboxStatus.Pending && x.LastModificationTime < cutoff).OrderBy(x => x.Id), ct);
            if (database.TenantId is null)
                await DeleteAsync(db => db.Set<DistributedEventRejection>()
                    .Where(x => x.Status != DistributedEventRejectionStatus.Pending && x.LastModificationTime < cutoff).OrderBy(x => x.Id), ct);
        }, cancellationToken);
        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0)
            throw new InvalidOperationException($"Distributed event retention failed in {result.FailedDatabases.Count} databases; {result.UnresolvedTenants.Count} tenants unresolved.");
    }

    private async Task DeleteAsync<TEntity>(Func<TDbContext, IQueryable<TEntity>> select, CancellationToken ct) where TEntity : class
    {
        while (true)
        {
            using var unit = units.Begin(requiresNew: true);
            var db = await contexts.GetDbContextAsync(ct);
            var batch = await select(db).Take(500).ToListAsync(ct);
            if (batch.Count == 0) return;
            db.RemoveRange(batch);
            await unit.CompleteAsync(ct);
            if (batch.Count < 500) return;
        }
    }
}
