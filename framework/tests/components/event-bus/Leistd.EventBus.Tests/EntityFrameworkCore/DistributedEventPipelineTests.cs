using System.Diagnostics;
using System.Text.Json;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.MultiTenancy.Context;
using Leistd.Tracing.Abstractions;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>真实工作单元、SQLite 唯一约束与提交后队列的发布和持久接收链路。</summary>
public sealed class DistributedEventPipelineTests
{
    /// <summary>D1：真实回环、原元数据、链路、关联标识与投影共事务。</summary>
    [Fact]
    public async Task A_committed_event_reaches_the_loopback_consumer_with_original_metadata_and_trace()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(loopback: true);
        using var listener = new ActivityListener { ShouldListenTo = s => s.Name == DistributedEventDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData };
        ActivitySource.AddActivityListener(listener);
        using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var message = new PaidEvent { OrderId = 7, OccurredOn = host.Clock.GetUtcNow().UtcDateTime };
        await host.InTenantAsync(DistributedEventTestHost.TenantA, async (sp, _) =>
        {
            using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change("request-123");
            await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
            Assert.Equal(0, host.Queue.Count);
        });
        Assert.Equal(1, host.Queue.Count);
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(message.EventId, host.Behavior.Event?.EventId);
        Assert.Equal(message.OccurredOn, host.Behavior.Event?.OccurredOn);
        Assert.Equal(DistributedEventTestHost.TenantA, host.Behavior.Tenant);
        Assert.Equal(request.TraceId, host.Behavior.TraceId);
        Assert.Equal("request-123", host.Behavior.CorrelationId);
        Assert.Equal(DistributedEventInboxStatus.Processed,
            (await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().SingleAsync())).Status);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
    }

    /// <summary>D2：没有边界拒绝发布；回滚不留记录也不入队。</summary>
    [Fact]
    public async Task Publishing_requires_an_active_unit_and_rollback_leaves_no_outbox_or_queue_item()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventBus>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new PaidEvent()));
        using (var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            await publisher.PublishAsync(new PaidEvent());
            await unit.SaveChangesAsync();
            await unit.RollbackAsync();
        }
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync()));
        Assert.Equal(0, host.Queue.Count);
    }

    /// <summary>载荷在发布时冻结；提交后队列读取数据库生成的非零 ID。</summary>
    [Fact]
    public async Task Publishing_freezes_the_runtime_payload_and_dispatch_uses_the_generated_id()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var message = new PaidEvent { OrderId = 7 };
        await host.InTenantAsync(null, async (sp, _) =>
        { await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(message); message.OrderId = 8; });
        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.True(row.Id > 0);
        Assert.Equal(7, JsonDocument.Parse(row.Payload).RootElement.GetProperty("orderId").GetInt32());
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddMinutes(1), row.NextAttemptAt);
        await host.Queue.RunAllAsync(host.Services);
        Assert.Single(host.Transport.Sent);
        Assert.Equal(DistributedEventOutboxStatus.Sent, (await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).Status);
    }

    /// <summary>D6、D13：原始载荷不必带元数据，重复接收只递增去重次数。</summary>
    [Fact]
    public async Task Receiving_restores_authoritative_metadata_and_duplicates_do_not_enqueue_again()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var raw = host.Raw(DistributedEventTestHost.TenantA, "{\"orderId\":7,\"EventId\":\"00000000-0000-0000-0000-000000000000\"}");
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        await receiver.ReceiveAsync(raw, default);
        await receiver.ReceiveAsync(raw, default);
        Assert.Equal(1, host.Queue.Count);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().SingleAsync());
        Assert.Equal(1, row.DuplicateCount);
        Assert.Equal(DistributedEventTestHost.TenantA, row.TenantId);
        Assert.Equal(row.EventId, JsonDocument.Parse(row.Payload).RootElement.GetProperty("eventId").GetGuid());
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(row.EventId, host.Behavior.Event?.EventId);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
    }

    /// <summary>D9：毒消息进入宿主拒收，原始字节与原因保留。</summary>
    [Theory]
    [InlineData("decode", DistributedEventRejectionReasons.InvalidEnvelope)]
    [InlineData("tenant", DistributedEventRejectionReasons.UnknownTenant)]
    [InlineData("payload", DistributedEventRejectionReasons.InvalidPayload)]
    public async Task Invalid_messages_are_durably_rejected_without_throwing(string kind, string reason)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var raw = kind == "decode" ? host.Raw() with { Body = new byte[] { 1, 2, 3 } }
            : host.Raw(kind == "tenant" ? Guid.NewGuid() : null, kind == "payload" ? "[]" : "{}");
        await using var scope = host.Services.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.TenantA);
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(raw, default);
        var row = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        Assert.Equal(reason, row.Reason);
        Assert.Equal(raw.Body.ToArray(), row.RawBody);
        Assert.Equal(raw.Coordinates, row.Coordinates);
        Assert.Equal(0, host.Queue.Count);
        Assert.Equal(DistributedEventTestHost.TenantA, scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Id);
    }

    /// <summary>D7：处理失败回滚投影写入，在新事务中安排重试；再次成功只留一笔投影。</summary>
    [Fact]
    public async Task A_failed_handler_rolls_back_business_changes_and_records_retry_in_another_transaction()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        host.Behavior.Throw = true;
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(0, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(DistributedEventInboxStatus.Pending, row.Status);
        Assert.NotNull(row.NextAttemptAt);
        host.Behavior.Throw = false;
        await scope.ServiceProvider.GetRequiredService<DistributedEventInboxProcessor<EventTestDbContext>>().ProcessAsync(null, row.Id, null, default);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
        Assert.Equal(DistributedEventInboxStatus.Processed, (await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).Status);
    }

    /// <summary>D4、D5：永久失败直接死信；瞬态在窗口内退避、超过窗口后死信。</summary>
    [Fact]
    public async Task Transport_classification_and_the_retry_window_determine_outbox_terminal_state()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "offline" });
        await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent()));
        await host.Queue.RunAllAsync(host.Services);
        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.Equal(DistributedEventOutboxStatus.Pending, row.Status);
        Assert.InRange((row.NextAttemptAt!.Value - host.Clock.GetUtcNow().UtcDateTime).TotalSeconds, 1.8, 2.2);
        host.Clock.Advance(TimeSpan.FromDays(2));
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "offline" });
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DistributedEventOutboxProcessor<EventTestDbContext>>().ProcessAsync(null, row.Id, null, default);
        row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.Equal(DistributedEventOutboxStatus.DeadLettered, row.Status);
        Assert.Equal(DistributedEventOutboxFailureCodes.RetryWindowExceeded, row.LastErrorCode);
    }

    /// <summary>D15：显式关闭立即发送与处理时只持久写入。</summary>
    [Fact]
    public async Task Disabled_process_roles_do_not_enqueue_committed_records()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => { o.Outbox.DispatchEnabled = false; o.Inbox.ProcessingEnabled = false; });
        await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent()));
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        Assert.Equal(0, host.Queue.Count);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync()));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));
    }
}
