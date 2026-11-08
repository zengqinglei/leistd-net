using System.Collections.Concurrent;
using System.Diagnostics;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.Tracing.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>发布、立即发送／重试与消费恢复同一个链路和独立关联标识。</summary>
[Collection(nameof(DistributedEventTraceQueryCollection))]
public sealed class DistributedEventSendTraceTests
{
    /// <summary>发送不使用当前扫描链路，也不覆盖持久消息头。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sends_restore_the_publish_context_for_immediate_and_scan_retry_paths(bool retry)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DistributedEventDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        var observed = new List<(ActivityTraceId Trace, ActivitySpanId Parent, string? Correlation)>();
        host.Transport.Sending = () => observed.Add((Activity.Current!.TraceId, Activity.Current.ParentSpanId,
            host.Services.GetRequiredService<ICorrelationIdProvider>().Get()));
        using (var caller = new Activity("publish-caller").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            await host.InTenantAsync(null, async (sp, _) =>
            {
                using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change("Publish_request_27");
                await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent());
            });
        }
        if (retry) host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "offline" });
        using var other = new Activity("unrelated-scanner").SetIdFormat(ActivityIdFormat.W3C).Start();
        await host.Queue.RunAllAsync(host.Services);
        if (retry)
        {
            host.Clock.Advance(TimeSpan.FromMinutes(2));
            await using var scan = host.Services.CreateAsyncScope();
            await scan.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(true, default);
        }
        var persisted = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        var published = Assert.Single(activities, x => x.OperationName == "event.publish");
        Assert.Equal(retry ? 2 : 1, observed.Count);
        Assert.All(observed, value =>
        { Assert.Equal(published.TraceId, value.Trace); Assert.Equal(published.SpanId, value.Parent); Assert.Equal("Publish_request_27", value.Correlation); });
        Assert.All(activities.Where(x => x.OperationName == "event.send"), send =>
        { Assert.Equal(ActivityKind.Producer, send.Kind); Assert.Equal(published.TraceId, send.TraceId); Assert.Equal(published.SpanId, send.ParentSpanId); });
        Assert.Equal(observed.Count, activities.Count(x => x.OperationName == "event.send"));
        var sent = host.Transport.Sent.Last();
        Assert.Equal(persisted.Headers.OrderBy(x => x.Key), sent.Headers.OrderBy(x => x.Key));
        await using var receive = host.Services.CreateAsyncScope();
        await receive.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(new()
        { Body = DistributedEventCodec.Encode(sent), Headers = sent.Headers, Coordinates = new Dictionary<string, string>(), ReceivedAt = host.Clock.GetUtcNow().UtcDateTime }, default);
        await host.Queue.RunAllAsync(host.Services);
        var processed = Assert.Single(activities, x => x.OperationName == "event.process");
        Assert.Equal(published.TraceId, processed.TraceId);
        Assert.Equal(published.SpanId, processed.ParentSpanId);
        Assert.Equal(published.TraceId, host.Behavior.TraceId);
        Assert.Equal("Publish_request_27", host.Behavior.CorrelationId);
        Assert.Equal(other.TraceId, Activity.Current?.TraceId);
    }
}
