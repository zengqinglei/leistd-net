using System.Text.Json;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>D16：收件箱与拒收也跨越 500 条批次边界，不把恢复结果灌进立即队列。</summary>
public sealed class DistributedEventBulkRecoveryTests
{
    /// <summary>收件箱按条件重放 1,200 条，重置每轮预算并保留迟到记录。</summary>
    [Fact]
    public async Task Filter_replay_resets_1200_terminal_records_in_batches_and_excludes_late_records()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var before = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            for (var index = 0; index < 1201; index++)
            {
                var message = new PaidEvent { OrderId = index, OccurredOn = before };
                db.Add(new DistributedEventInbox
                {
                    EventId = message.EventId, EventName = "order.paid", Consumer = "orders.projection",
                    Payload = JsonSerializer.Serialize(message, JsonSerializerOptions.Web), OccurredAt = before,
                    ReceivedAt = index == 1200 ? before.AddSeconds(1) : before, LastModificationTime = before,
                    Status = DistributedEventInboxStatus.Failed, AttemptCount = 5, LastErrorCode = "handler-exception"
                });
            }
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().ReplayAsync(new()
        { Filter = new() { Before = before, Status = DistributedEventInboxStatus.Failed } });
        Assert.Equal(1200, result.AffectedCount); Assert.Empty(result.Skipped); Assert.Equal(0, host.Queue.Count);
        var rows = await host.ReadAsync(db => db.Set<DistributedEventInbox>().ToListAsync());
        Assert.Equal(1200, rows.Count(x => x.Status == DistributedEventInboxStatus.Pending && x.AttemptCount == 0 && x.ReplayCount == 1));
        Assert.Single(rows, x => x.Status == DistributedEventInboxStatus.Failed);
        Assert.Equal(new long[] { 500, 500, 200 }, host.ManagedEvents.Select(x => x.AffectedCount));
        Assert.All(host.ManagedEvents, e => Assert.Equal(DistributedEventManagementAction.Replayed, e.Action));
    }

    /// <summary>拒收按条件重新解码 1,200 条，先写收件箱再分批标记，保留后续新拒收。</summary>
    [Fact]
    public async Task Filter_redecode_accepts_1200_saved_raw_messages_without_flooding_the_immediate_queue()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var before = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            for (var index = 0; index < 1201; index++)
                db.Add(new DistributedEventRejection
                {
                    Transport = "scripted", Reason = "sdk-decoder-missing", Error = "Decoder was unavailable.",
                    RawBody = host.Raw().Body.ToArray(), ReceivedAt = index == 1200 ? before.AddSeconds(1) : before,
                    LastModificationTime = before
                });
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().RedecodeAsync(new()
        { Filter = new() { Before = before, Reason = "sdk-decoder-missing" } });
        Assert.Equal(1200, result.AffectedCount); Assert.Empty(result.Skipped); Assert.Equal(0, host.Queue.Count);
        Assert.Equal(1200, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));
        Assert.Equal(1200, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync(x => x.Status == DistributedEventRejectionStatus.Redecoded)));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync(x => x.Status == DistributedEventRejectionStatus.Pending)));
        Assert.Equal(new long[] { 500, 500, 200 }, host.ManagedEvents.Select(x => x.AffectedCount));
        Assert.All(host.ManagedEvents, e => Assert.Equal(DistributedEventManagementAction.Redecoded, e.Action));
    }
}
