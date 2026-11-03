using System.Reflection;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore.Retention;

// 按物理库逐个清理：IgnoreQueryFilters 只能放开同一个库里的租户。
// 先删早于保留期的投递及其尝试，再删早于保留期、已没有投递的事件；都按批，每批一个工作单元，加载后删除。
// 开关在登记时交给调度器；有库失败时抛出，调度器不记水位，下一个调度时段按截止时间重做。
internal sealed class WebhookRetentionJob<TDbContext>(
    IOptionsSnapshot<WebhookRetentionOptions> retention,
    ITenantDatabaseRunner databaseRunner,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    TimeProvider timeProvider,
    ILogger<WebhookRetentionJob<TDbContext>> logger) : IRecurringJob
    where TDbContext : DbContext
{
    internal const string Name = "webhooks.retention";

    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        var current = retention.Value;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-current.Days);
        var deliveries = 0;
        var events = 0;

        // 停用租户的库照样清理
        var result = await databaseRunner.ForEachDatabaseAsync(ConnectionStringName, activeOnly: false, async (_, ct) =>
        {
            deliveries += await DeleteDeliveriesAsync(cutoff, current.BatchSize, ct);
            events += await DeleteEventsAsync(cutoff, current.BatchSize, ct);
        }, cancellationToken);

        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0)
        {
            throw new InvalidOperationException(
                $"Deleted {deliveries} expired webhook delivery(ies) and {events} event(s); "
                + $"{result.FailedDatabases.Count} of {result.Databases} database(s) failed, "
                + $"{result.UnresolvedTenants.Count} tenant(s) could not be resolved to a database.");
        }

        logger.LogInformation(
            "Deleted {Deliveries} expired webhook delivery(ies) and {Events} event(s) across {Databases} database(s).",
            deliveries, events, result.Databases);
    }

    private async Task<int> DeleteDeliveriesAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
            var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

            // IgnoreQueryFilters：同库其余租户的记录一并清理
            var batch = await dbContext.Set<WebhookDelivery>()
                .IgnoreQueryFilters()
                .Where(x => x.CreationTime < cutoff)
                .OrderBy(x => x.CreationTime)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return total;
            }

            var ids = batch.Select(x => x.Id).ToArray();
            var attempts = await dbContext.Set<WebhookAttempt>()
                .IgnoreQueryFilters()
                .Where(x => ids.Contains(x.DeliveryId))
                .ToListAsync(cancellationToken);

            dbContext.Set<WebhookAttempt>().RemoveRange(attempts);
            dbContext.Set<WebhookDelivery>().RemoveRange(batch);
            await unitOfWork.CompleteAsync(cancellationToken);

            total += batch.Count;
            if (batch.Count < batchSize)
            {
                return total;
            }
        }
    }

    // 只删已没有投递的事件：保留期内仍有投递引用的事件留到投递过期后再删
    private async Task<int> DeleteEventsAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
            var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

            var batch = await dbContext.Set<WebhookEvent>()
                .IgnoreQueryFilters()
                .Where(x => x.OccurredAt < cutoff
                            && !dbContext.Set<WebhookDelivery>().IgnoreQueryFilters().Any(d => d.EventId == x.Id))
                .OrderBy(x => x.OccurredAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return total;
            }

            dbContext.Set<WebhookEvent>().RemoveRange(batch);
            await unitOfWork.CompleteAsync(cancellationToken);

            total += batch.Count;
            if (batch.Count < batchSize)
            {
                return total;
            }
        }
    }
}
