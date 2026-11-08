using System.Reflection;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

internal sealed class DistributedEventScanner<TDbContext>(
    ITenantDatabaseRunner databases,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    IServiceScopeFactory scopes,
    IOptionsMonitor<DistributedEventOptions> options,
    TimeProvider clock,
    ILogger<DistributedEventScanner<TDbContext>> logger) where TDbContext : DbContext
{
    private static readonly string ConnectionName = typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;
    private const int BatchSize = 100;

    public async Task ScanAsync(bool outbox, CancellationToken ct)
    {
        if (outbox ? !options.CurrentValue.Outbox.DispatchEnabled : !options.CurrentValue.Inbox.ProcessingEnabled) return;
        var failures = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var result = await databases.ForEachDatabaseAsync(ConnectionName, activeOnly: false, async (database, token) =>
        {
            if (outbox) await WarnBacklogAsync(database.TenantId, now, token);
            long after = 0;
            while (true)
            {
                var batch = await ReadAsync(outbox, after, now, token);
                if (batch.Count == 0) break;
                await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (row, cancellation) =>
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        if (outbox) await scope.ServiceProvider.GetRequiredService<DistributedEventOutboxProcessor<TDbContext>>().ProcessAsync(row.TenantId, row.Id, null, cancellation);
                        else await scope.ServiceProvider.GetRequiredService<DistributedEventInboxProcessor<TDbContext>>().ProcessAsync(row.TenantId, row.Id, null, cancellation);
                    }
                    catch (Exception exception) when (!cancellation.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failures);
                        logger.LogError(exception, "Distributed event record {RecordId} of tenant {TenantId} could not be scanned.", row.Id, row.TenantId);
                    }
                });
                after = batch[^1].Id;
                if (batch.Count < BatchSize) break;
            }
        }, ct);
        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0 || failures > 0)
            throw new InvalidOperationException($"Distributed event scan failed: {failures} records, {result.FailedDatabases.Count} databases, {result.UnresolvedTenants.Count} unresolved tenants.");
    }

    private async Task<List<Due>> ReadAsync(bool outbox, long after, DateTime now, CancellationToken ct)
    {
        using var unit = units.Begin(requiresNew: true);
        var db = await contexts.GetDbContextAsync(ct);
        var rows = outbox
            ? await db.Set<DistributedEventOutbox>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Status == DistributedEventOutboxStatus.Pending && x.NextAttemptAt <= now && x.Id > after)
                .OrderBy(x => x.Id).Take(BatchSize).Select(x => new Due(x.Id, x.TenantId)).ToListAsync(ct)
            : await db.Set<DistributedEventInbox>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Status == DistributedEventInboxStatus.Pending && x.NextAttemptAt <= now && x.Id > after)
                .OrderBy(x => x.Id).Take(BatchSize).Select(x => new Due(x.Id, x.TenantId)).ToListAsync(ct);
        await unit.CompleteAsync(ct);
        return rows;
    }

    private async Task WarnBacklogAsync(Guid? database, DateTime now, CancellationToken ct)
    {
        using var unit = units.Begin(requiresNew: true);
        var db = await contexts.GetDbContextAsync(ct);
        var pending = db.Set<DistributedEventOutbox>().IgnoreQueryFilters().Where(x => x.Status == DistributedEventOutboxStatus.Pending);
        var oldest = await pending.Select(x => (DateTime?)x.CreationTime).MinAsync(ct);
        if (oldest is { } time && now - time >= options.CurrentValue.BacklogWarningAfter)
            logger.LogWarning(new EventId(4102, "DistributedEventOutboxBacklog"),
                "Distributed event outbox backlog in database {Database}: {Count} pending, oldest age {OldestAge}.",
                database?.ToString() ?? "host", await pending.LongCountAsync(ct), now - time);
        await unit.CompleteAsync(ct);
    }

    private sealed record Due(long Id, Guid? TenantId);
}

internal sealed class DistributedEventOutboxJob<TDbContext>(DistributedEventScanner<TDbContext> scanner) : IRecurringJob where TDbContext : DbContext
{
    internal const string Name = "event-bus.outbox";
    public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken) => scanner.ScanAsync(true, cancellationToken);
}

internal sealed class DistributedEventInboxJob<TDbContext>(DistributedEventScanner<TDbContext> scanner) : IRecurringJob where TDbContext : DbContext
{
    internal const string Name = "event-bus.inbox";
    public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken) => scanner.ScanAsync(false, cancellationToken);
}
