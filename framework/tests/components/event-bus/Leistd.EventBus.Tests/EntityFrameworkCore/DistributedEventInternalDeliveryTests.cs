using System.Collections.Concurrent;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Stores;
using Leistd.Tracing.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>本服务内事件不经传输，由派发进程在同库写本服务收件箱，其余语义与普通分布式事件一致。</summary>
public sealed class DistributedEventInternalDeliveryTests
{
    /// <summary>传输按"未登记即永久失败"编排，被调用就会进死信；本地投递须原样透传租户、排序键与消息头，并由真实处理器完成。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_internal_event_reaches_every_local_consumer_without_calling_the_transport(bool singleProcess)
    {
        var recorder = new ReviewRecorder();
        await using var worker = await DistributedEventTestHost.CreateAsync(configureServices: Consumers(recorder));
        worker.Transport.Results.Enqueue(UnregisteredEvent);
        await using var api = singleProcess ? null : await DistributedEventTestHost.CreateAsync(withTransport: false, shareDatabaseWith: worker,
            configure: o => { o.Outbox.DispatchEnabled = o.Inbox.ReceiveEnabled = o.Inbox.ProcessingEnabled = false; });
        var message = new ReviewEvent { OrderId = 7 };
        await (api ?? worker).InTenantAsync(DistributedEventTestHost.TenantA, async (sp, _) =>
        {
            using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change("request-123");
            await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
        });
        if (!singleProcess)
        {
            await using var scan = worker.Services.CreateAsyncScope();
            await scan.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(true, default);
        }
        await worker.Queue.RunAllAsync(worker.Services);

        Assert.Empty(worker.Transport.Sent);
        var outbox = await worker.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().SingleAsync());
        Assert.Equal(DistributedEventOutboxStatus.Sent, outbox.Status);
        Assert.Equal(DistributedEventInternalDelivery.Value, outbox.Headers[DistributedEventInternalDelivery.Header]);
        var inbox = await worker.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().OrderBy(x => x.Consumer).ToListAsync());
        Assert.Equal(new[] { "orders.review", "orders.review-audit" }, inbox.Select(x => x.Consumer));
        Assert.All(inbox, row =>
        {
            Assert.Equal(DistributedEventInboxStatus.Processed, row.Status);
            Assert.Equal(DistributedEventTestHost.TenantA, row.TenantId);
            Assert.Equal("order:7", row.OrderingKey);
            Assert.Equal(outbox.Headers, row.Headers);
            Assert.Equal("request-123", row.Headers[DistributedEventHeaders.CorrelationId]);
            Assert.Equal(DistributedEventInternalDelivery.Transport, row.Coordinates["transport"]);
        });
        Assert.Equal(2, recorder.Handled.Count);
        Assert.All(recorder.Handled, handled => Assert.Equal((message.EventId, (Guid?)DistributedEventTestHost.TenantA), (handled.EventId, handled.TenantId)));
    }

    /// <summary>派发端没有任何内部消费者时不能当作成功静默丢弃；补齐登记的实例重投后照常投递。</summary>
    [Fact]
    public async Task An_internal_event_without_any_local_consumer_dead_letters_until_redelivered_after_registration()
    {
        await using var unregistered = await DistributedEventTestHost.CreateAsync();
        unregistered.Transport.Results.Enqueue(UnregisteredEvent);
        await unregistered.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new ReviewEvent { OrderId = 7 }));
        await unregistered.Queue.RunAllAsync(unregistered.Services);
        var row = await unregistered.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.Equal(DistributedEventOutboxStatus.DeadLettered, row.Status);
        Assert.Equal(DistributedEventOutboxFailureCodes.InternalNoConsumer, row.LastErrorCode);
        Assert.Null(row.SentAt);
        Assert.Empty(unregistered.Transport.Sent);
        Assert.Equal(0, await unregistered.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));

        var recorder = new ReviewRecorder();
        await using var registered = await DistributedEventTestHost.CreateAsync(shareDatabaseWith: unregistered, configureServices: Consumers(recorder));
        await using (var scope = registered.Services.CreateAsyncScope())
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().RedeliverAsync(new() { Ids = [row.Id] })).AffectedCount);
        await registered.Queue.RunAllAsync(registered.Services);
        Assert.Equal(DistributedEventOutboxStatus.Sent, (await registered.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).Status);
        Assert.Equal(2, recorder.Handled.Count);
        Assert.Empty(registered.Transport.Sent);
    }

    /// <summary>载荷与租户的业务失败直接死信且不写拒收表；租户目录查询异常按瞬态退避，日志指明本地投递。</summary>
    [Theory]
    [InlineData("payload", DistributedEventOutboxFailureCodes.InternalInvalidPayload)]
    [InlineData("tenant", DistributedEventOutboxFailureCodes.InternalUnknownTenant)]
    [InlineData("directory", DistributedEventOutboxFailureCodes.TransportException)]
    public async Task Internal_delivery_failures_are_classified_without_writing_rejections(string kind, string code)
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: services =>
        {
            Consumers(new ReviewRecorder())(services);
            if (kind == "directory") services.AddSingleton<ITenantStore, FailingTenantStore>();
        });
        Guid? tenantId = kind switch { "tenant" => Guid.NewGuid(), "directory" => DistributedEventTestHost.TenantA, _ => null };
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.Add(new DistributedEventOutbox
            {
                EventId = Guid.NewGuid(), EventName = "order.reviewed", TenantId = tenantId, OccurredAt = now,
                Payload = kind == "payload" ? "[]" : "{\"orderId\":7}",
                Headers = new() { [DistributedEventInternalDelivery.Header] = DistributedEventInternalDelivery.Value },
                CreationTime = now, LastModificationTime = now, NextAttemptAt = now
            });
            return Task.CompletedTask;
        });
        var id = (await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().SingleAsync())).Id;
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DistributedEventOutboxProcessor<EventTestDbContext>>().ProcessAsync(tenantId, id, null, default);

        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().SingleAsync());
        Assert.Equal(code, row.LastErrorCode);
        Assert.Equal(kind == "directory" ? DistributedEventOutboxStatus.Pending : DistributedEventOutboxStatus.DeadLettered, row.Status);
        if (kind == "directory") Assert.True(row.NextAttemptAt > host.Clock.GetUtcNow().UtcDateTime, "A transient local delivery failure must back off.");
        else Assert.Null(row.NextAttemptAt);
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync()));
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().CountAsync()));
        Assert.Empty(host.Transport.Sent);
        if (kind == "directory")
            Assert.Contains(host.Logs.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Message.Contains("local delivery", StringComparison.Ordinal));
    }

    /// <summary>收件箱已提交、发件箱未标记时再次投递，只增加重复计数，不新增收件箱行或处理任务。</summary>
    [Fact]
    public async Task Delivering_again_after_the_inbox_commit_only_counts_duplicates()
    {
        var recorder = new ReviewRecorder();
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: Consumers(recorder));
        await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new ReviewEvent { OrderId = 7 }));
        await host.Queue.RunAllAsync(host.Services);
        await host.InTenantAsync(null, async (_, db) =>
        {
            var row = await db.Set<DistributedEventOutbox>().SingleAsync();
            row.Status = DistributedEventOutboxStatus.Pending;
            row.SentAt = null;
            row.NextAttemptAt = host.Clock.GetUtcNow().UtcDateTime;
        });
        var id = (await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).Id;
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DistributedEventOutboxProcessor<EventTestDbContext>>().ProcessAsync(null, id, null, default);

        var inbox = await host.ReadAsync(db => db.Set<DistributedEventInbox>().ToListAsync());
        Assert.Equal(2, inbox.Count);
        Assert.All(inbox, row => Assert.Equal(1, row.DuplicateCount));
        Assert.Equal(0, host.Queue.Count);
        Assert.Equal(2, recorder.Handled.Count);
        Assert.Equal(DistributedEventOutboxStatus.Sent, (await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).Status);
    }

    /// <summary>同键内部事件都进入收件箱后，前序处理失败保持 Pending 时后序不执行，前序成功后才轮到后序。</summary>
    [Fact]
    public async Task Inbox_rows_of_internal_events_with_one_ordering_key_wait_for_the_failed_predecessor()
    {
        var recorder = new ReviewRecorder();
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: Consumers(recorder));
        var first = new ReviewEvent { OrderId = 7 };
        var second = new ReviewEvent { OrderId = 7 };
        recorder.FailFor = first.EventId;
        foreach (var message in new[] { first, second })
            await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(message));
        await host.Queue.RunAllAsync(host.Services);

        var rows = await host.ReadAsync(db => db.Set<DistributedEventInbox>().Where(x => x.Consumer == "orders.review").OrderBy(x => x.Id).ToListAsync());
        Assert.Equal(new[] { first.EventId, second.EventId }, rows.Select(x => x.EventId));
        Assert.All(rows, row => Assert.Equal(DistributedEventInboxStatus.Pending, row.Status));
        Assert.Equal(new[] { 1, 0 }, rows.Select(x => x.AttemptCount));
        Assert.Empty(recorder.Handled);

        recorder.FailFor = null;
        await using var scope = host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<DistributedEventInboxProcessor<EventTestDbContext>>();
        await processor.ProcessAsync(null, rows[1].Id, null, default);
        Assert.Empty(recorder.Handled);
        await processor.ProcessAsync(null, rows[0].Id, null, default);
        await processor.ProcessAsync(null, rows[1].Id, null, default);
        Assert.Equal(new[] { first.EventId, second.EventId }, recorder.Handled.Select(x => x.EventId));
    }

    /// <summary>详情可见内部标记与坐标，重放进入同一处理器，事件目录列出已登记的内部消费者。</summary>
    [Fact]
    public async Task Management_shows_internal_routing_replays_the_same_handler_and_lists_internal_consumers()
    {
        var recorder = new ReviewRecorder();
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: Consumers(recorder));
        await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new ReviewEvent { OrderId = 7 }));
        await host.Queue.RunAllAsync(host.Services);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();

        var outbox = Assert.Single((await service.GetOutboxListAsync(new())).Items);
        Assert.Equal(DistributedEventInternalDelivery.Value, (await service.GetOutboxAsync(outbox.Id)).Headers[DistributedEventInternalDelivery.Header]);
        var inbox = Assert.Single((await service.GetInboxListAsync(new() { Consumer = "orders.review" })).Items);
        Assert.Equal(DistributedEventInternalDelivery.Transport, (await service.GetInboxAsync(inbox.Id)).Coordinates["transport"]);
        Assert.Equal(1, (await service.ReplayAsync(new() { Ids = [inbox.Id] })).AffectedCount);
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(3, recorder.Handled.Count);
        Assert.Equal(2, recorder.Handled.Count(x => x.Consumer == "orders.review"));
        var entry = Assert.Single(await service.GetCatalogAsync(), x => x.EventName == "order.reviewed");
        Assert.True(entry.Incoming);
        Assert.Equal(new[] { "orders.review", "orders.review-audit" }, entry.Consumers);
    }

    /// <summary>传输看到的订阅不含本服务内事件；经传输入口到达的同名消息不进内部消费者的收件箱，也不算拒收。</summary>
    [Fact]
    public async Task A_transport_message_with_an_internal_event_name_reaches_no_internal_consumer()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: Consumers(new ReviewRecorder()));
        await using var scope = host.Services.CreateAsyncScope();
        var transportView = scope.ServiceProvider.GetRequiredService<IDistributedEventSubscriptions>().GetAll();
        Assert.Contains(transportView, x => x.EventName == "order.paid");
        Assert.DoesNotContain(transportView, x => x.EventName == "order.reviewed");

        var now = host.Clock.GetUtcNow().UtcDateTime;
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(new()
        {
            Body = DistributedEventCodec.Encode(new() { EventId = Guid.NewGuid(), EventName = "order.reviewed", OccurredAt = now,
                Payload = "{\"orderId\":7}", Headers = new Dictionary<string, string>() }),
            Headers = new Dictionary<string, string>(), Coordinates = new Dictionary<string, string> { ["topic"] = "order.reviewed" }, ReceivedAt = now
        }, default);
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync()));
        Assert.Equal(0, host.Queue.Count);
    }

    private static readonly DistributedEventSendResult UnregisteredEvent = new()
    {
        Status = DistributedEventSendStatus.PermanentFailure, ErrorCode = "event-not-registered", Error = "The transport does not know this event."
    };

    private static Action<IServiceCollection> Consumers(ReviewRecorder recorder) => services =>
    {
        services.AddSingleton(recorder);
        services.AddDistributedEventHandler<ReviewHandler>("orders.review");
        services.AddDistributedEventHandler<ReviewAuditHandler>("orders.review-audit");
    };

    /// <summary>只由本服务消费的有序事件。</summary>
    [EventName("order.reviewed")]
    public sealed class ReviewEvent : DistributedEvent, IInternalDistributedEvent, IOrderedDistributedEvent
    {
        /// <summary>业务标识。</summary>
        public int OrderId { get; set; }
        /// <inheritdoc />
        public string OrderingKey => "order:" + OrderId;
    }

    /// <summary>两个内部消费者观察到的处理。</summary>
    public sealed class ReviewRecorder
    {
        /// <summary>按处理顺序记录。</summary>
        public ConcurrentQueue<(string Consumer, Guid EventId, Guid? TenantId)> Handled { get; } = new();
        /// <summary>处理该事件时让两个消费者都失败。</summary>
        public Guid? FailFor { get; set; }
    }

    /// <summary>第一个内部消费者。</summary>
    public sealed class ReviewHandler(ReviewRecorder recorder, ICurrentTenant tenant) : IEventHandler<ReviewEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ReviewEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event.EventId == recorder.FailFor) throw new InvalidOperationException("The review could not be processed.");
            recorder.Handled.Enqueue(("orders.review", @event.EventId, tenant.Id));
            return Task.CompletedTask;
        }
    }

    /// <summary>第二个内部消费者。</summary>
    public sealed class ReviewAuditHandler(ReviewRecorder recorder, ICurrentTenant tenant) : IEventHandler<ReviewEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(ReviewEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event.EventId == recorder.FailFor) throw new InvalidOperationException("The review could not be processed.");
            recorder.Handled.Enqueue(("orders.review-audit", @event.EventId, tenant.Id));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTenantStore : ITenantStore
    {
        public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The tenant directory is unavailable.");
        public Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The tenant directory is unavailable.");
    }
}
