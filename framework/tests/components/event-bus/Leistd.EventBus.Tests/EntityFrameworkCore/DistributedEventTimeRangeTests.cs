using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>列表与按条件恢复的时间范围：闭区间、单边、缺省，以及与加载截止叠加。</summary>
public sealed class DistributedEventTimeRangeTests
{
    /// <summary>发件箱按创建时间筛选，两端都包含；上限取 EndTime 与 Before 中较早者。</summary>
    [Fact]
    public async Task Outbox_list_filters_creation_time_inclusively_and_combines_with_before()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var (early, middle) = (now.AddHours(-2), now.AddHours(-1));
        await host.InTenantAsync(null, (_, db) =>
        {
            db.AddRange(DeadLetter(early), DeadLetter(middle), DeadLetter(now));
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();

        async Task<long> Count(DateTime? start, DateTime? end, DateTime? before = null)
            => (await service.GetOutboxListAsync(new() { StartTime = start, EndTime = end, Before = before })).TotalCount;

        Assert.Equal(3, await Count(null, null));
        Assert.Equal(2, await Count(middle, now));
        Assert.Equal(2, await Count(middle, null));
        Assert.Equal(2, await Count(null, middle));
        Assert.Equal(1, await Count(middle, middle));
        Assert.Equal(2, await Count(early, now, before: middle));
        Assert.Equal(0, await Count(now, null, before: middle));
    }

    /// <summary>收件箱与拒收按接收时间筛选，与发件箱同一套边界。</summary>
    [Fact]
    public async Task Inbox_and_rejection_lists_filter_received_time_inclusively()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var (early, middle) = (now.AddHours(-2), now.AddHours(-1));
        await host.InTenantAsync(null, (_, db) =>
        {
            foreach (var time in new[] { early, middle, now })
            {
                db.Add(FailedInbox(time));
                db.Add(PendingRejection(host, time));
            }

            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();

        Assert.Equal(2, (await service.GetInboxListAsync(new() { StartTime = middle, EndTime = now })).TotalCount);
        Assert.Equal(1, (await service.GetInboxListAsync(new() { EndTime = early })).TotalCount);
        Assert.Equal(2, (await service.GetRejectionsAsync(new() { StartTime = early, EndTime = middle })).TotalCount);
        Assert.Equal(1, (await service.GetRejectionsAsync(new() { StartTime = now })).TotalCount);
    }

    /// <summary>按条件恢复只改范围内的记录，审计里的筛选带上时间范围。</summary>
    [Fact]
    public async Task Filter_recovery_only_changes_records_inside_the_time_range()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var (early, middle) = (now.AddHours(-2), now.AddHours(-1));
        await host.InTenantAsync(null, (_, db) =>
        {
            foreach (var time in new[] { early, middle, now })
            {
                db.Add(DeadLetter(time));
                db.Add(FailedInbox(time));
                db.Add(PendingRejection(host, time));
            }

            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();

        var redelivered = await service.RedeliverAsync(new() { Filter = new() { Before = now, StartTime = middle, EndTime = now } });
        var replayed = await service.ReplayAsync(new()
        { Filter = new() { Before = now, StartTime = early, EndTime = early, Status = DistributedEventInboxStatus.Failed } });
        var discarded = await service.DiscardAsync(new() { Filter = new() { Before = now, EndTime = middle } });

        Assert.Equal(2, redelivered.AffectedCount);
        Assert.Equal(1, replayed.AffectedCount);
        Assert.Equal(2, discarded.AffectedCount);
        var outbox = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().ToListAsync());
        Assert.Equal(DistributedEventOutboxStatus.DeadLettered, Assert.Single(outbox, x => x.CreationTime == early).Status);
        var inbox = await host.ReadAsync(db => db.Set<DistributedEventInbox>().ToListAsync());
        Assert.Equal(DistributedEventInboxStatus.Pending, Assert.Single(inbox, x => x.ReceivedAt == early).Status);
        var rejections = await host.ReadAsync(db => db.Set<DistributedEventRejection>().ToListAsync());
        Assert.Equal(DistributedEventRejectionStatus.Pending, Assert.Single(rejections, x => x.ReceivedAt == now).Status);
        var audit = host.ManagedEvents.First(x => x.Action == DistributedEventManagementAction.Redelivered);
        Assert.Equal(middle, JsonDocument.Parse(audit.FilterJson!).RootElement.GetProperty("startTime").GetDateTime());
    }

    /// <summary>列表与恢复在管理用例入口拒绝非 UTC 时间和颠倒的起止。</summary>
    [Theory]
    [InlineData("local-start")]
    [InlineData("unspecified-end")]
    [InlineData("reversed")]
    public async Task Lists_and_filter_recovery_reject_non_utc_or_reversed_ranges(string mistake)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var (start, end) = mistake switch
        {
            "local-start" => (DateTime.SpecifyKind(now, DateTimeKind.Local), now),
            "unspecified-end" => (now.AddHours(-1), DateTime.SpecifyKind(now, DateTimeKind.Unspecified)),
            _ => (now, now.AddHours(-1))
        };
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();

        await Assert.ThrowsAsync<ValidationException>(() => service.GetOutboxListAsync(new() { StartTime = start, EndTime = end }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetInboxListAsync(new() { StartTime = start, EndTime = end }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetRejectionsAsync(new() { StartTime = start, EndTime = end }));
        await Assert.ThrowsAsync<ValidationException>(() => service.RedeliverAsync(new()
        { Filter = new() { Before = now, StartTime = start, EndTime = end } }));
        await Assert.ThrowsAsync<ValidationException>(() => service.ReplayAsync(new()
        { Filter = new() { Before = now, StartTime = start, EndTime = end, Status = DistributedEventInboxStatus.Failed } }));
        await Assert.ThrowsAsync<ValidationException>(() => service.DiscardAsync(new()
        { Filter = new() { Before = now, StartTime = start, EndTime = end } }));
    }

    private static DistributedEventOutbox DeadLetter(DateTime time) => new()
    {
        EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", OccurredAt = time,
        CreationTime = time, LastModificationTime = time, FirstFailureAt = time.AddDays(-2),
        Status = DistributedEventOutboxStatus.DeadLettered, AttemptCount = 38, LastErrorCode = "offline"
    };

    private static DistributedEventInbox FailedInbox(DateTime time)
    {
        var message = new PaidEvent { OrderId = 1, OccurredOn = time };
        return new DistributedEventInbox
        {
            EventId = message.EventId, EventName = "order.paid", Consumer = "orders.projection",
            Payload = JsonSerializer.Serialize(message, JsonSerializerOptions.Web), OccurredAt = time,
            ReceivedAt = time, LastModificationTime = time,
            Status = DistributedEventInboxStatus.Failed, AttemptCount = 5, LastErrorCode = "handler-exception"
        };
    }

    private static DistributedEventRejection PendingRejection(DistributedEventTestHost host, DateTime time) => new()
    {
        Transport = "scripted", Reason = "sdk-decoder-missing", Error = "Decoder was unavailable.",
        RawBody = host.Raw().Body.ToArray(), ReceivedAt = time, LastModificationTime = time
    };
}
