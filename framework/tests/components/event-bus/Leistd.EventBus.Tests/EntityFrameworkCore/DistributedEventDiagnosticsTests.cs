using System.Text.Json;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>外部异步映射与稳定日志事件，依赖故障不伪装成已确认接收。</summary>
public sealed class DistributedEventDiagnosticsTests
{
    /// <summary>D13：外部业务载荷没有框架消息头，异步 DecodeAsync 仍可映射原始 ID、时间和租户。</summary>
    [Fact]
    public async Task An_asynchronous_external_decoder_maps_business_metadata_without_framework_headers()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var id = Guid.NewGuid();
        var time = host.Clock.GetUtcNow().UtcDateTime;
        host.Transport.Decoder = async (raw, ct) =>
        {
            await Task.Yield(); ct.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(raw.Body);
            var json = document.RootElement;
            return new IncomingDistributedEvent
            {
                EventId = json.GetProperty("businessId").GetGuid(), EventName = "order.paid",
                TenantId = json.GetProperty("tenant").GetGuid(), OccurredAt = json.GetProperty("happenedAt").GetDateTime(),
                Payload = json.GetProperty("data").GetRawText(), Headers = new Dictionary<string, string>(), Coordinates = raw.Coordinates
            };
        };
        var raw = new RawDistributedEvent
        {
            Body = JsonSerializer.SerializeToUtf8Bytes(new { businessId = id, tenant = DistributedEventTestHost.TenantA, happenedAt = time, data = new { orderId = 7 } }),
            Headers = new Dictionary<string, string>(), Coordinates = new Dictionary<string, string> { ["externalId"] = "message-123" }, ReceivedAt = time
        };
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(raw, default);
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(id, host.Behavior.Event?.EventId);
        Assert.Equal(time, host.Behavior.Event?.OccurredOn);
        Assert.Equal(DistributedEventTestHost.TenantA, host.Behavior.Tenant);
    }

    /// <summary>D14：拒收表写入失败须记录稳定 Error 事件后继续抛出，供传输重投。</summary>
    [Fact]
    public async Task Rejection_persistence_failure_logs_the_stable_event_and_propagates()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(null, (_, db) => db.Database.ExecuteSqlRawAsync("DROP TABLE DistributedEventRejection"));
        await using var scope = host.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>()
            .RejectAsync(host.Raw(), "sdk-bad-message", "Invalid message.", default));
        Assert.Contains(host.Logs.GetSnapshot(), x => x.Id.Name == "DistributedEventRejectPersistFailed" && x.Level == LogLevel.Error);
    }

    /// <summary>死信、消费耗尽、积压与拒收均发出约定名称，不能只断言状态而漏掉运维接入点。</summary>
    [Fact]
    public async Task Terminal_failures_backlog_and_rejection_emit_the_documented_log_events()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.MaxAttempts = 1);
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.PermanentFailure, ErrorCode = "message-too-large" });
        await host.InTenantAsync(null, async (sp, _) =>
        {
            var bus = sp.GetRequiredService<IDistributedEventBus>();
            await bus.PublishAsync(new PaidEvent { OrderId = 7 });
            await bus.PublishAsync(new PaidEvent { OrderId = 8 });
        });
        await host.Queue.RunAllAsync(host.Services);
        host.Behavior.Throw = true;
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        await receiver.ReceiveAsync(host.Raw(), default);
        await receiver.RejectAsync(host.Raw(), DistributedEventRejectionReasons.InvalidPayload, "Bad payload.", default);
        await host.Queue.RunAllAsync(host.Services);
        await host.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent { OrderId = 9 }));
        host.Queue.Drop();
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        await scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(true, default);
        var names = host.Logs.GetSnapshot().Select(x => x.Id.Name).ToArray();
        Assert.Contains("DistributedEventDeadLettered", names);
        Assert.Contains("DistributedEventConsumeFailed", names);
        Assert.Contains("DistributedEventOutboxBacklog", names);
        Assert.Contains("DistributedEventRejected", names);
        foreach (var (id, name) in new[] { (4101, "DistributedEventDeadLettered"), (4103, "DistributedEventConsumeFailed") })
        {
            var entry = Assert.Single(host.Logs.GetSnapshot(), x => x.Id.Id == id);
            Assert.Equal(name, entry.Id.Name);
            Assert.Equal("order.paid", entry.GetStructuredStateValue("EventName"));
            Assert.True(Guid.TryParse(entry.GetStructuredStateValue("EventId"), out _));
        }
    }

    /// <summary>拒收成功与写库失败均带完整入站上下文，坐标有序且最长 256 字符。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Rejection_logs_include_structured_inbound_metadata_and_bounded_coordinates(bool failPersistence, bool longCoordinates)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        if (failPersistence)
            await host.InTenantAsync(null, (_, db) => db.Database.ExecuteSqlRawAsync("DROP TABLE DistributedEventRejection"));
        var id = Guid.NewGuid();
        var raw = host.Raw(payload: "not-json", id: id) with
        {
            Coordinates = new Dictionary<string, string> { ["z"] = "last", ["a"] = longCoordinates ? new string('x', 300) : "first" }
        };
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        if (failPersistence) await Assert.ThrowsAsync<DbUpdateException>(() => receiver.ReceiveAsync(raw, default));
        else await receiver.ReceiveAsync(raw, default);
        var entry = Assert.Single(host.Logs.GetSnapshot(), x => x.Id.Id == (failPersistence ? 4105 : 4104));
        Assert.Equal(failPersistence ? "DistributedEventRejectPersistFailed" : "DistributedEventRejected", entry.Id.Name);
        Assert.Equal("scripted", entry.GetStructuredStateValue("Transport"));
        Assert.Equal(DistributedEventRejectionReasons.InvalidPayload, entry.GetStructuredStateValue("Reason"));
        Assert.Equal("order.paid", entry.GetStructuredStateValue("EventName"));
        Assert.Equal(id.ToString(), entry.GetStructuredStateValue("EventId"));
        Assert.Equal(longCoordinates ? "a=" + new string('x', 254) : "a=first;z=last", entry.GetStructuredStateValue("Coordinates"));
        if (failPersistence) Assert.IsType<DbUpdateException>(entry.Exception);
    }
}
