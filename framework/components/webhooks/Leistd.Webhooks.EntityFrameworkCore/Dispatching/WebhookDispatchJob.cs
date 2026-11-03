using System.Reflection;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.Webhooks.EntityFrameworkCore.Dispatching;

// 规格"投递链路"第 3 步，兜底扫描：按物理库逐个进入，库内放开租户过滤取到期的待投递记录，
// 每条在所属租户的上下文与独立作用域里处理，有界并发。集群锁保证同时只有一个副本在扫，不做行级抢占。
// 某个库或某条投递失败时记日志、继续其余，本轮结束后抛出汇总异常，让任务记为失败。
internal sealed class WebhookDispatchJob<TDbContext>(
    ITenantDatabaseRunner databaseRunner,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WebhookDispatchJob<TDbContext>> logger) : IRecurringJob
    where TDbContext : DbContext
{
    internal const string Name = "webhooks.dispatch";

    // 每批取回的到期记录数与同时发出的请求数
    internal const int BatchSize = 100;
    internal const int MaxConcurrency = 8;

    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var processed = 0;
        var failedDeliveries = 0;

        // 停用租户的库照样扫描：已提交的事件是发件箱，发布时租户是启用的
        var result = await databaseRunner.ForEachDatabaseAsync(ConnectionStringName, activeOnly: false, async (_, ct) =>
        {
            var (count, failures) = await DispatchCurrentDatabaseAsync(now, ct);
            Interlocked.Add(ref processed, count);
            Interlocked.Add(ref failedDeliveries, failures);
        }, cancellationToken);

        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0 || failedDeliveries > 0)
        {
            throw new InvalidOperationException(
                $"Dispatched {processed} due webhook delivery(ies); {failedDeliveries} failed to process, "
                + $"{result.FailedDatabases.Count} of {result.Databases} database(s) failed, "
                + $"{result.UnresolvedTenants.Count} tenant(s) could not be resolved to a database.");
        }

        if (processed > 0)
        {
            logger.LogInformation("Dispatched {Count} due webhook delivery(ies) across {Databases} database(s).", processed, result.Databases);
        }
    }

    private async Task<(int Processed, int Failures)> DispatchCurrentDatabaseAsync(DateTime now, CancellationToken cancellationToken)
    {
        var processed = 0;
        var failures = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await ReadDueAsync(now, after, cancellationToken);
            if (batch.Count == 0)
            {
                return (processed, failures);
            }

            await Parallel.ForEachAsync(
                batch,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
                async (due, ct) =>
                {
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor<TDbContext>>()
                            .ProcessAsync(due.TenantId, due.Id, expectedAttemptCount: null, ct);
                    }
                    catch (Exception exception) when (!ct.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failures);
                        logger.LogError(exception, "Webhook delivery {DeliveryId} of tenant {TenantId} could not be processed.", due.Id, due.TenantId);
                    }
                });

            processed += batch.Count;
            if (batch.Count < BatchSize)
            {
                return (processed, failures);
            }

            // 按 Id 续读：处理失败仍到期的记录不会在同一轮被反复取回
            after = batch[^1].Id;
        }
    }

    // 库内放开租户过滤：每个库只切到一个代表租户，同库其余租户的投递要一并取到，并带上各自的 TenantId
    private async Task<List<DueDelivery>> ReadDueAsync(DateTime now, Guid? after, CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        var query = dbContext.Set<WebhookDelivery>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Status == WebhookDeliveryStatus.Pending && x.NextAttemptAt <= now);
        if (after is { } last)
        {
            // EF 把 CompareTo 与 0 的比较改写为列比较，与下面的 OrderBy 同一排序口径
            query = query.Where(x => x.Id.CompareTo(last) > 0);
        }

        var batch = await query
            .OrderBy(x => x.Id)
            .Take(BatchSize)
            .Select(x => new DueDelivery(x.Id, x.TenantId))
            .ToListAsync(cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return batch;
    }

    private sealed record DueDelivery(Guid Id, Guid? TenantId);
}
