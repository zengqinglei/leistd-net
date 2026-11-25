using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>宿主替换订阅接口后，普通事件的接收、处理与目录都按替换后的订阅执行。</summary>
public sealed class DistributedEventSubscriptionReplacementTests
{
    /// <summary>替换实现只提供另一个消费者名；传输入口为它入库，收件箱处理器为它执行处理器，目录列出它。</summary>
    [Fact]
    public async Task A_replaced_subscription_source_drives_receiving_processing_and_the_catalog()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IDistributedEventSubscriptions>(new ReplacedSubscriptions())));
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        await host.Queue.RunAllAsync(host.Services);

        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal("orders.replaced", row.Consumer);
        Assert.Equal(DistributedEventInboxStatus.Processed, row.Status);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
        var entry = Assert.Single(await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().GetCatalogAsync(), x => x.EventName == "order.paid");
        Assert.Equal(new[] { "orders.replaced" }, entry.Consumers);
    }

    private sealed class ReplacedSubscriptions : IDistributedEventSubscriptions
    {
        public IReadOnlyList<DistributedEventSubscription> GetAll() =>
        [
            new() { EventName = "order.paid", EventType = typeof(PaidEvent), HandlerType = typeof(PaidHandler), Consumer = "orders.replaced" }
        ];
    }
}
