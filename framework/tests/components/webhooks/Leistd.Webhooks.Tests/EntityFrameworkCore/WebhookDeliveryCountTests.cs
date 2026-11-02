using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Stores;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>投递统计：按创建时间分桶、按当前状态计数、只看当前租户；存量快照；保留期下限。</summary>
public sealed class WebhookDeliveryCountTests : IAsyncLifetime
{
    private static readonly DateTime Day1 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);
    private static readonly DateTime Day3 = Day1.AddDays(2);

    private WebhookTestHost _host = null!;
    private Guid _first;
    private Guid _second;

    public async Task InitializeAsync()
    {
        _host = await WebhookTestHost.CreateAsync();
        _first = (await _host.AddSubscriptionAsync(null)).Subscription.Id;
        _second = (await _host.AddSubscriptionAsync(null)).Subscription.Id;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// 每桶的总数与成功、失败、重试中按当前状态计；边界上的投递归后一个桶，没有投递的桶为 0；
    /// 同库其他租户的投递不计入，按订阅统计只数该订阅。
    /// </summary>
    [Fact]
    public async Task Buckets_count_deliveries_by_creation_time_and_current_status()
    {
        await SeedAsync(null,
            (_first, Day1.AddHours(1), WebhookDeliveryStatus.Succeeded),
            (_first, Day1.AddHours(2), WebhookDeliveryStatus.Failed),
            (_second, Day1.AddHours(3), WebhookDeliveryStatus.Pending),
            (_second, Day2, WebhookDeliveryStatus.Succeeded));
        await SeedAsync(WebhookTestHost.TenantA, (_first, Day1.AddHours(1), WebhookDeliveryStatus.Succeeded));

        var all = await CountAsync(null, Day1, Day2, Day3, Day3.AddDays(1));
        var first = await CountAsync(_first, Day1, Day2);

        Assert.Equal(
            [new WebhookDeliveryCounts(3, 1, 1, 1), new WebhookDeliveryCounts(1, 1, 0, 0), new WebhookDeliveryCounts(0, 0, 0, 0)],
            all);
        Assert.Equal([new WebhookDeliveryCounts(2, 1, 1, 0)], first);
    }

    /// <summary>边界不足两个或不严格递增是调用方的错，抛出而不是静默给 0。</summary>
    [Fact]
    public async Task Invalid_bucket_edges_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CountAsync(null, Day1));
        await Assert.ThrowsAsync<ArgumentException>(() => CountAsync(null, Day2, Day1));
    }

    /// <summary>存量：重试中的投递数与已停用订阅数，不随区间变化；按订阅统计时只看该订阅。</summary>
    [Fact]
    public async Task The_snapshot_counts_pending_deliveries_and_disabled_subscriptions()
    {
        await SeedAsync(null,
            (_first, Day1, WebhookDeliveryStatus.Pending),
            (_first, Day2, WebhookDeliveryStatus.Pending),
            (_second, Day2, WebhookDeliveryStatus.Pending),
            (_second, Day3, WebhookDeliveryStatus.Failed));
        await _host.InTenantAsync(null, async (_, db) => (await db.Set<WebhookSubscription>().FindAsync(_second))!.IsEnabled = false);

        var all = await ReadAsync(null, reader => reader.GetSnapshotAsync());
        var first = await ReadAsync(null, reader => reader.GetSnapshotAsync(_first));

        Assert.Equal(new WebhookDeliverySnapshot(3, 1), all);
        Assert.Equal(new WebhookDeliverySnapshot(2, 0), first);
    }

    /// <summary>登记了保留期时下限为"现在 − 天数"；关闭保留期或没有登记保留期任务时为空，统计不据此判定上一区间。</summary>
    [Fact]
    public async Task The_retained_since_bound_follows_the_retention_registration()
    {
        Assert.Equal(_host.Now.AddDays(-62), await ReadAsync(null, reader => Task.FromResult(reader.GetRetainedSince())));

        await using var disabled = await WebhookTestHost.CreateAsync(settings: new Dictionary<string, string?>
        {
            ["Leistd:Webhooks:Retention:Enabled"] = "false"
        });
        Assert.Null(disabled.Services.CreateScope().ServiceProvider.GetRequiredService<IWebhookDeliveryReader>().GetRetainedSince());

        await using var unregistered = await WebhookTestHost.CreateAsync(services =>
        {
            var marker = services.Single(d => d.ServiceType.Name == "WebhookRetentionRegistration");
            services.Remove(marker);
        });
        Assert.Null(unregistered.Services.CreateScope().ServiceProvider.GetRequiredService<IWebhookDeliveryReader>().GetRetainedSince());
    }

    private Task<IReadOnlyList<WebhookDeliveryCounts>> CountAsync(Guid? subscriptionId, params DateTime[] edges)
        => ReadAsync(null, reader => reader.CountByBucketsAsync(edges, subscriptionId));

    private Task<T> ReadAsync<T>(Guid? tenantId, Func<IWebhookDeliveryReader, Task<T>> read)
        => _host.InTenantAsync(tenantId, (provider, _) => read(provider.GetRequiredService<IWebhookDeliveryReader>()));

    private Task SeedAsync(Guid? tenantId, params (Guid SubscriptionId, DateTime CreationTime, WebhookDeliveryStatus Status)[] rows)
        => _host.InTenantAsync(tenantId, (_, db) =>
        {
            var webhookEvent = new WebhookEvent { TenantId = tenantId, Type = WebhookTestHost.OrderPaid, Payload = "{}", OccurredAt = Day1 };
            db.Add(webhookEvent);
            db.AddRange(rows.Select(row => new WebhookDelivery
            {
                TenantId = tenantId,
                EventId = webhookEvent.Id,
                SubscriptionId = row.SubscriptionId,
                Status = row.Status,
                CreationTime = row.CreationTime
            }));
            return Task.CompletedTask;
        });
}
