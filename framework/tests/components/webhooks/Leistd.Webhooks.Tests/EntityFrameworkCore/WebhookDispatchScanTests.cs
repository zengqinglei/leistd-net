using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Tests.TestDoubles;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>A7：兜底扫描按物理库进入、库内放开租户过滤，每条投递在自己的租户上下文里处理；有库失败时本轮失败。</summary>
public sealed class WebhookDispatchScanTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>共享库里的宿主、租户 A、租户 B 与专属库租户各有到期投递；立即投递全部丢失后，扫描把四方都发出。</summary>
    [Fact]
    public async Task The_scan_sends_due_deliveries_of_every_tenant_in_every_database()
    {
        Guid?[] tenants = [null, WebhookTestHost.TenantA, WebhookTestHost.TenantB, WebhookTestHost.DedicatedTenant];
        var subscriptions = new Dictionary<Guid, Guid?>();
        foreach (var tenant in tenants)
        {
            subscriptions[(await _host.AddSubscriptionAsync(tenant)).Subscription.Id] = tenant;
            await _host.PublishAsync(tenant);
        }

        _host.Queue.Drop();
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.RunDispatchAsync();

        var sent = _host.Sender.Sent.ToArray();
        Assert.Equal(4, sent.Length);
        Assert.All(sent, x => Assert.Equal(subscriptions[x.Request.SubscriptionId], x.TenantId));

        var deliveries = (await _host.DeliveriesAsync()).Concat(await _host.DeliveriesAsync(_host.DedicatedConnectionString)).ToList();
        Assert.Equal(4, deliveries.Count);
        Assert.All(deliveries, x => Assert.Equal(WebhookDeliveryStatus.Succeeded, x.Status));
    }

    /// <summary>宽限期内（立即投递尚可能在途）的新投递不被扫描抢发。</summary>
    [Fact]
    public async Task Deliveries_within_the_immediate_dispatch_grace_are_left_to_the_queue()
    {
        await _host.AddSubscriptionAsync(WebhookTestHost.TenantA);
        await _host.PublishAsync(WebhookTestHost.TenantA);

        _host.Clock.Advance(TimeSpan.FromSeconds(59));
        await _host.RunDispatchAsync();

        Assert.Empty(_host.Sender.Sent);
    }

    /// <summary>某个库失败时其余库照样发出，本轮结束后抛出，任务记为失败而不是成功。</summary>
    [Fact]
    public async Task A_failing_database_fails_the_run_after_the_other_databases_are_dispatched()
    {
        await _host.AddSubscriptionAsync(WebhookTestHost.TenantA);
        await _host.PublishAsync(WebhookTestHost.TenantA);
        _host.Queue.Drop();
        _host.BreakDedicatedDatabase();

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(_host.RunDispatchAsync);

        Assert.Single(_host.Sender.Sent);
    }

    /// <summary>解析不出连接的租户与失败的库一样让本轮失败：那个租户的投递一条都没发。</summary>
    [Fact]
    public async Task An_unresolved_tenant_fails_the_run()
    {
        _host.Databases.Unresolved.Add(new TenantDatabaseFailure(Guid.NewGuid(), "ring rotated"));

        await Assert.ThrowsAsync<InvalidOperationException>(_host.RunDispatchAsync);
    }
}
