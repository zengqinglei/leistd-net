using System.Text.Json;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Publishing;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>发布器：在调用方的工作单元里写事件与投递，只投给本租户的匹配订阅，提交后才入队。</summary>
public sealed class WebhookPublisherTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>A12：租户 A 发布的事件只投给 A 的订阅；同库的 B 与宿主的同类订阅都不产生投递。</summary>
    [Fact]
    public async Task An_event_is_delivered_only_to_subscriptions_of_the_publishing_tenant()
    {
        var a = await _host.AddSubscriptionAsync(WebhookTestHost.TenantA);
        await _host.AddSubscriptionAsync(WebhookTestHost.TenantB);
        await _host.AddSubscriptionAsync(null);

        await _host.PublishAsync(WebhookTestHost.TenantA);

        var delivery = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(a.Subscription.Id, delivery.SubscriptionId);
        Assert.Equal(WebhookTestHost.TenantA, delivery.TenantId);
    }

    /// <summary>按类型匹配：订阅了别的类型、已停用的订阅不产生投递；"*" 订阅全部类型。</summary>
    [Fact]
    public async Task Only_enabled_subscriptions_for_the_type_or_all_events_are_matched()
    {
        var paid = await _host.AddSubscriptionAsync(null, eventTypes: WebhookTestHost.OrderPaid);
        var all = await _host.AddSubscriptionAsync(null, eventTypes: WebhookDefinition.AllEventTypes);
        await _host.AddSubscriptionAsync(null, eventTypes: WebhookTestHost.OrderShipped);
        var disabled = await _host.AddSubscriptionAsync(null);
        await _host.InTenantAsync(null, async (_, db) =>
            (await db.Set<WebhookSubscription>().SingleAsync(x => x.Id == disabled.Subscription.Id)).IsEnabled = false);

        await _host.PublishAsync(null);

        var matched = (await _host.DeliveriesAsync()).Select(x => x.SubscriptionId).Order().ToArray();
        Assert.Equal(new[] { paid.Subscription.Id, all.Subscription.Id }.Order().ToArray(), matched);
    }

    /// <summary>新投递待发、自动触发、留 1 分钟宽限给立即投递；载荷是发布时冻结的 camelCase 信封。</summary>
    [Fact]
    public async Task The_event_payload_is_frozen_and_new_deliveries_leave_a_grace_period_for_immediate_dispatch()
    {
        await _host.AddSubscriptionAsync(null);
        var data = new Order { OrderId = 7 };

        await _host.InTenantAsync(null, async (provider, _) =>
        {
            await provider.GetRequiredService<IWebhookPublisher>().PublishAsync(WebhookTestHost.OrderPaid, data);
            data.OrderId = 8;
        });

        var webhookEvent = await _host.ReadAsync(db => db.Set<WebhookEvent>().SingleAsync());
        using var payload = JsonDocument.Parse(webhookEvent.Payload);
        Assert.Equal(WebhookTestHost.OrderPaid, payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(7, payload.RootElement.GetProperty("data").GetProperty("orderId").GetInt32());
        Assert.Equal(_host.Now, payload.RootElement.GetProperty("timestamp").GetDateTimeOffset().UtcDateTime);

        var delivery = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(webhookEvent.Id, delivery.EventId);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(WebhookAttemptTrigger.Automatic, delivery.NextTrigger);
        Assert.Equal(_host.Now.AddMinutes(1), delivery.NextAttemptAt);
    }

    /// <summary>A2：回滚时不留事件与投递、不入队；入队只发生在提交之后，提交前队列里什么都没有。</summary>
    [Fact]
    public async Task Rolling_back_leaves_nothing_and_only_a_commit_enqueues_the_deliveries()
    {
        await _host.AddSubscriptionAsync(null);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            await scope.ServiceProvider.GetRequiredService<IWebhookPublisher>().PublishAsync(WebhookTestHost.OrderPaid, null);
            await unitOfWork.RollbackAsync();
        }

        Assert.Empty(await _host.DeliveriesAsync());
        Assert.Equal(0, await _host.ReadAsync(db => db.Set<WebhookEvent>().CountAsync()));
        Assert.Equal(0, _host.Queue.Count);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            await scope.ServiceProvider.GetRequiredService<IWebhookPublisher>().PublishAsync(WebhookTestHost.OrderPaid, null);
            Assert.Equal(0, _host.Queue.Count);
            await unitOfWork.CompleteAsync();
        }

        Assert.Equal(1, _host.Queue.Count);
    }

    /// <summary>A2：没有活动工作单元时抛出，不自开事务冒充业务事务；未登记或空白的类型是参数错误。</summary>
    [Fact]
    public async Task Publishing_requires_a_unit_of_work_and_a_defined_event_type()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IWebhookPublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(WebhookTestHost.OrderPaid, null));
        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync("order.unknown", null));
        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(" ", null));
    }

    /// <summary>入队项带着投递所属的租户：执行时不依赖还原入队时的环境上下文，也在该租户里处理。</summary>
    [Fact]
    public async Task Queued_deliveries_are_processed_in_the_tenant_that_published_them()
    {
        await _host.AddSubscriptionAsync(WebhookTestHost.DedicatedTenant);
        await _host.PublishAsync(WebhookTestHost.DedicatedTenant);

        await _host.Queue.RunAllAsync(_host.Services);

        var sent = Assert.Single(_host.Sender.Sent);
        Assert.Equal(WebhookTestHost.DedicatedTenant, sent.TenantId);
        Assert.Equal(WebhookDeliveryStatus.Succeeded,
            Assert.Single(await _host.DeliveriesAsync(_host.DedicatedConnectionString)).Status);
        Assert.Null(_host.Services.GetRequiredService<ICurrentTenant>().Id);
    }

    private sealed class Order
    {
        public int OrderId { get; set; }
    }
}
