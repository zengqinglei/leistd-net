using Leistd.Data.Querying;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Options;
using Leistd.Webhooks.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore.Stores;

// 投递统计的读取：租户隔离由 IMultiTenant 的全局查询过滤器承担，与管理查询同一口径。
// retention 只在宿主调用了 AddWebhookRetention 时存在，此时保留期下限才有意义。
internal sealed class EfCoreWebhookDeliveryReader<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    TimeProvider timeProvider,
    WebhookRetentionRegistration? retention = null) : IWebhookDeliveryReader
    where TDbContext : DbContext
{
    public async Task<IReadOnlyList<WebhookDeliveryCounts>> CountByBucketsAsync(
        IReadOnlyList<DateTime> edges,
        Guid? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var deliveries = await DeliveriesAsync(subscriptionId, cancellationToken);

        // 区间筛选与分组由 GroupByTimeBucket 按首尾边界完成；分组与计数在数据库里，每个有记录的桶只取回一行
        var rows = await deliveries
            .GroupByTimeBucket(x => x.CreationTime, edges)
            .Select(group => new
            {
                Bucket = group.Key,
                Total = group.Count(),
                Succeeded = group.Count(x => x.Status == WebhookDeliveryStatus.Succeeded),
                Failed = group.Count(x => x.Status == WebhookDeliveryStatus.Failed),
                Pending = group.Count(x => x.Status == WebhookDeliveryStatus.Pending)
            })
            .ToListAsync(cancellationToken);

        var counts = new WebhookDeliveryCounts[edges.Count - 1];
        Array.Fill(counts, new WebhookDeliveryCounts(0, 0, 0, 0));
        foreach (var row in rows)
        {
            counts[row.Bucket] = new WebhookDeliveryCounts(row.Total, row.Succeeded, row.Failed, row.Pending);
        }

        return counts;
    }

    public async Task<WebhookDeliverySnapshot> GetSnapshotAsync(Guid? subscriptionId = null, CancellationToken cancellationToken = default)
    {
        var pending = await (await DeliveriesAsync(subscriptionId, cancellationToken))
            .CountAsync(x => x.Status == WebhookDeliveryStatus.Pending, cancellationToken);

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscriptions = dbContext.Set<WebhookSubscription>().AsNoTracking();
        if (subscriptionId is { } id)
        {
            subscriptions = subscriptions.Where(x => x.Id == id);
        }

        var disabled = await subscriptions.CountAsync(x => !x.IsEnabled, cancellationToken);
        return new WebhookDeliverySnapshot(pending, disabled);
    }

    public DateTime? GetRetainedSince()
        => retention?.Options.CurrentValue is { Enabled: true, Days: var days }
            ? timeProvider.GetUtcNow().UtcDateTime.AddDays(-days)
            : null;

    private async Task<IQueryable<WebhookDelivery>> DeliveriesAsync(Guid? subscriptionId, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var query = dbContext.Set<WebhookDelivery>().AsNoTracking();
        return subscriptionId is { } id ? query.Where(x => x.SubscriptionId == id) : query;
    }
}

// AddWebhookRetention 登记的标记：保留期任务确实登记了，统计才报告保留期下限。
internal sealed class WebhookRetentionRegistration(IOptionsMonitor<WebhookRetentionOptions> options)
{
    public IOptionsMonitor<WebhookRetentionOptions> Options { get; } = options;
}
