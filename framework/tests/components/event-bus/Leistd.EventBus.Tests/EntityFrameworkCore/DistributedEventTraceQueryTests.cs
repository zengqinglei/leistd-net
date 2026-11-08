using System.Diagnostics;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.MultiTenancy.Context;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>链路与关联标识经过真实发布、接收、拒收持久化后均可精确检索。</summary>
[Collection(nameof(DistributedEventTraceQueryCollection))]
public sealed class DistributedEventTraceQueryTests
{
    private const string TraceId = "abcdef0123456789abcdef0123456789";
    private const string Parent = "00-" + TraceId + "-1234567890abcdef-01";
    private const string CorrelationId = "Caller_ABC-request-27";

    /// <summary>显式关联标识与链路不同，两种标识命中三类列表，租户范围保持有效。</summary>
    [Fact]
    public async Task Trace_and_distinct_correlation_ids_find_persisted_records_without_crossing_tenant_scope()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        using (var activity = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).SetParentId(Parent).Start())
        {
            foreach (var tenant in new[] { DistributedEventTestHost.TenantA, DistributedEventTestHost.TenantB })
                await host.InTenantAsync(tenant, async (sp, _) =>
                {
                    using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change(CorrelationId);
                    await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent { OrderId = 27 });
                });
        }
        await host.Queue.RunAllAsync(host.Services);
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        foreach (var sent in host.Transport.Sent)
            await receiver.ReceiveAsync(new()
            {
                Body = DistributedEventCodec.Encode(sent), Headers = sent.Headers,
                Coordinates = new Dictionary<string, string>(), ReceivedAt = host.Clock.GetUtcNow().UtcDateTime
            }, default);
        var raw = host.Raw(headers: new Dictionary<string, string>
        { [DistributedEventHeaders.TraceParent] = Parent, [DistributedEventHeaders.CorrelationId] = CorrelationId });
        await receiver.ReceiveAsync(raw with { Body = new byte[] { 1, 2 } }, default);
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        foreach (var keyword in new[] { TraceId.ToUpperInvariant(), CorrelationId })
        {
            Assert.Equal(2, (await service.GetOutboxListAsync(new() { Keyword = keyword })).TotalCount);
            Assert.Equal(2, (await service.GetInboxListAsync(new() { Keyword = keyword })).TotalCount);
            Assert.Single((await service.GetRejectionsAsync(new() { Keyword = keyword })).Items);
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.TenantA);
            Assert.Equal(DistributedEventTestHost.TenantA, Assert.Single((await service.GetOutboxListAsync(new() { Keyword = keyword })).Items).TenantId);
            Assert.Equal(DistributedEventTestHost.TenantA, Assert.Single((await service.GetInboxListAsync(new() { Keyword = keyword })).Items).TenantId);
        }
        foreach (var keyword in new[] { CorrelationId[..^1], CorrelationId.ToLowerInvariant(), TraceId[..^1] })
        {
            Assert.Empty((await service.GetOutboxListAsync(new() { Keyword = keyword })).Items);
            Assert.Empty((await service.GetInboxListAsync(new() { Keyword = keyword })).Items);
            Assert.Empty((await service.GetRejectionsAsync(new() { Keyword = keyword })).Items);
        }
        Assert.All(await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().ToListAsync()), row =>
        { Assert.Equal(TraceId, row.TraceId); Assert.Equal(CorrelationId, row.CorrelationId); });
        Assert.All(await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().ToListAsync()), row =>
        { Assert.Equal(TraceId, row.TraceId); Assert.Equal(CorrelationId, row.CorrelationId); });
        var rejection = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        Assert.Equal(TraceId, rejection.TraceId); Assert.Equal(CorrelationId, rejection.CorrelationId);
    }

    /// <summary>缺失或非法 traceparent 不生成链路列，仍可通过独立关联标识检索。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("bad-parent")]
    [InlineData("00-00000000000000000000000000000000-1234567890abcdef-01")]
    [InlineData("00-abcdef0123456789abcdef0123456789-0000000000000000-01")]
    [InlineData("ff-abcdef0123456789abcdef0123456789-1234567890abcdef-01")]
    public async Task Correlation_only_incoming_messages_remain_searchable_when_traceparent_is_missing_or_invalid(string? parent)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var headers = new Dictionary<string, string> { [DistributedEventHeaders.CorrelationId] = CorrelationId };
        if (parent is not null) headers[DistributedEventHeaders.TraceParent] = parent;
        var raw = host.Raw(headers: headers);
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        await receiver.ReceiveAsync(raw, default);
        await receiver.ReceiveAsync(raw with { Body = new byte[] { 1, 2 } }, default);
        var inbox = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        var rejection = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        Assert.Null(inbox.TraceId); Assert.Equal(CorrelationId, inbox.CorrelationId);
        Assert.Null(rejection.TraceId); Assert.Equal(CorrelationId, rejection.CorrelationId);
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        Assert.Single((await service.GetInboxListAsync(new() { Keyword = CorrelationId })).Items);
        Assert.Single((await service.GetRejectionsAsync(new() { Keyword = CorrelationId })).Items);
    }

    /// <summary>无 Activity 的写入入口保留显式关联标识，列宽与关联组件常量一致且建索引。</summary>
    [Fact]
    public async Task Publishing_without_an_activity_persists_and_queries_the_correlation_header()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.Outbox.DispatchEnabled = false);
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            await host.InTenantAsync(null, async (sp, db) =>
            {
                using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change(CorrelationId);
                await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent());
                foreach (var type in new[] { typeof(DistributedEventOutbox), typeof(DistributedEventInbox), typeof(DistributedEventRejection) })
                {
                    var entity = db.Model.FindEntityType(type)!;
                    Assert.Equal(CorrelationIdConstants.MaxLength, entity.FindProperty("CorrelationId")!.GetMaxLength());
                    Assert.Contains(entity.GetIndexes(), index => index.Properties.Count == 1 && index.Properties[0].Name == "CorrelationId");
                }
            });
        }
        finally { Activity.Current = previous; }
        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.Null(row.TraceId); Assert.Equal(CorrelationId, row.CorrelationId);
        await using var scope = host.Services.CreateAsyncScope();
        Assert.Single((await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>()
            .GetOutboxListAsync(new() { Keyword = CorrelationId })).Items);
    }

    /// <summary>同一列表筛选用于批量恢复时仍匹配两种标识，不扩大到其他记录。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_recovery_uses_the_same_exact_trace_or_correlation_match_as_the_lists(bool correlation)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            foreach (var match in new[] { true, false })
            {
                var trace = match ? TraceId : new string('1', 32);
                var correlated = match ? CorrelationId : CorrelationId + "-other";
                db.Add(new DistributedEventOutbox { EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}",
                    Status = DistributedEventOutboxStatus.DeadLettered, CreationTime = now, OccurredAt = now,
                    LastModificationTime = now, TraceId = trace, CorrelationId = correlated });
                db.Add(new DistributedEventInbox { EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", Consumer = "orders.projection",
                    Status = DistributedEventInboxStatus.Failed, ReceivedAt = now, OccurredAt = now,
                    LastModificationTime = now, TraceId = trace, CorrelationId = correlated });
                db.Add(new DistributedEventRejection { Transport = "scripted", Reason = "invalid-payload", Error = "Invalid payload.", RawBody = [],
                    ReceivedAt = now, LastModificationTime = now, TraceId = trace, CorrelationId = correlated });
            }
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var key = correlation ? CorrelationId : TraceId.ToUpperInvariant();
        Assert.Equal(1, (await service.RedeliverAsync(new() { Filter = new() { Before = now, Keyword = key } })).AffectedCount);
        Assert.Equal(1, (await service.ReplayAsync(new() { Filter = new() { Before = now, Keyword = key, Status = DistributedEventInboxStatus.Failed } })).AffectedCount);
        Assert.Equal(1, (await service.DiscardAsync(new() { Filter = new() { Before = now, Keyword = key } })).AffectedCount);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync(x => x.Status == DistributedEventOutboxStatus.DeadLettered)));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync(x => x.Status == DistributedEventInboxStatus.Failed)));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync(x => x.Status == DistributedEventRejectionStatus.Pending)));
    }
}

/// <summary>隔离其他测试的全局 ActivityListener，保留无监听器入口的真实语义。</summary>
[CollectionDefinition(nameof(DistributedEventTraceQueryCollection), DisableParallelization = true)]
public sealed class DistributedEventTraceQueryCollection;
