using Leistd.DependencyInjection.DynamicProxy.Registration;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Options;
using Microsoft.Extensions.Options;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>仅发布宿主必须能通过严格容器验证并真实启动。</summary>
public sealed class DistributedEventWriterOnlyTests
{
    /// <summary>关闭三项执行职责后无需传输，发布与查询继续可用。</summary>
    [Fact]
    public async Task A_writer_without_transport_validates_starts_and_publishes()
    {
        ServiceDescriptor[] registrations = [];
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: false, validateServices: true, configure: o =>
        { o.Outbox.DispatchEnabled = o.Inbox.ReceiveEnabled = o.Inbox.ProcessingEnabled = false; },
            configureServices: services => registrations = services.ToArray());
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        foreach (var descriptor in registrations) builder.Services.Add(descriptor);
        builder.ConfigureContainer(new DynamicProxyServiceRegistrationCallbackFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        using var runtime = builder.Build();
        Assert.Null(runtime.Services.GetService<IDistributedEventTransport>());
        await runtime.StartAsync();
        try
        {
            await using var scope = runtime.Services.CreateAsyncScope();
            var management = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
            await management.SendTestEventAsync();
            Assert.Equal(1, (await management.GetStatusAsync()).PendingOutbox);
            Assert.Equal(0, host.Queue.Count);
        }
        finally { await runtime.StopAsync(); }
    }
    /// <summary>三项职责分别打开时，启动校验指出实际配置键。</summary>
    [Theory]
    [InlineData("Outbox:DispatchEnabled")]
    [InlineData("Inbox:ReceiveEnabled")]
    [InlineData("Inbox:ProcessingEnabled")]
    public async Task An_enabled_role_without_transport_reports_its_configuration_key(string key)
    {
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: false, configure: o =>
        {
            o.Outbox.DispatchEnabled = key == "Outbox:DispatchEnabled";
            o.Inbox.ReceiveEnabled = key == "Inbox:ReceiveEnabled";
            o.Inbox.ProcessingEnabled = key == "Inbox:ProcessingEnabled";
        });
        var error = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<DistributedEventOptions>>().Value);
        Assert.Contains(DistributedEventOptions.SectionName + ":" + key, error.Message);
    }

    /// <summary>只写宿主不解析传输也可执行三类恢复及读取。</summary>
    [Fact]
    public async Task A_writer_can_query_redeliver_replay_and_discard_without_resolving_a_transport()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: false, configure: o =>
        { o.Outbox.DispatchEnabled = o.Inbox.ReceiveEnabled = o.Inbox.ProcessingEnabled = false; });
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.Add(new DistributedEventOutbox { EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}",
                Status = DistributedEventOutboxStatus.DeadLettered, OccurredAt = now, CreationTime = now, LastModificationTime = now });
            db.Add(new DistributedEventInbox { EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", Consumer = "orders.projection",
                Status = DistributedEventInboxStatus.Failed, OccurredAt = now, ReceivedAt = now, LastModificationTime = now });
            db.Add(new DistributedEventRejection { Transport = "scripted", Reason = "invalid-envelope", Error = "Safe reason.", ReceivedAt = now, LastModificationTime = now });
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var outgoing = Assert.Single((await service.GetOutboxListAsync(new())).Items);
        var incoming = Assert.Single((await service.GetInboxListAsync(new())).Items);
        var rejected = Assert.Single((await service.GetRejectionsAsync(new())).Items);
        Assert.Equal(1, (await service.RedeliverAsync(new() { Ids = [outgoing.Id] })).AffectedCount);
        Assert.Equal(1, (await service.ReplayAsync(new() { Ids = [incoming.Id] })).AffectedCount);
        Assert.Equal(1, (await service.DiscardAsync(new() { Ids = [rejected.Id] })).AffectedCount);
        Assert.Equal(0, host.Queue.Count);
        Assert.Equal(3, host.ManagedEvents.Count);
    }

}
