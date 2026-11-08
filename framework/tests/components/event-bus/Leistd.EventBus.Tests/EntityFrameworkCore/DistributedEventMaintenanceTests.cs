using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Retention;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>扫描跨轮保序、进程分工与终态保留期。</summary>
public sealed class DistributedEventMaintenanceTests
{
    /// <summary>D3：前序 Pending 即使尚未到期也阻止同键；前序成功后后续轮次才能发送。</summary>
    [Fact]
    public async Task A_pending_predecessor_blocks_the_same_key_across_scans_until_it_is_sent()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "offline" });
        await host.InTenantAsync(null, async (sp, _) =>
        {
            var bus = sp.GetRequiredService<IDistributedEventBus>();
            await bus.PublishAsync(new PaidEvent { OrderId = 7 });
            await bus.PublishAsync(new PaidEvent { OrderId = 7 });
        });
        await host.Queue.RunAllAsync(host.Services);
        Assert.Single(host.Transport.Sent);
        await using var scope = host.Services.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>();
        await scanner.ScanAsync(true, default);
        Assert.Single(host.Transport.Sent);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "offline" });
        await scanner.ScanAsync(true, default);
        Assert.Equal(2, host.Transport.Sent.Count);
        Assert.Single(host.Transport.Sent.Select(x => x.EventId).Distinct());
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await scanner.ScanAsync(true, default);
        await scanner.ScanAsync(true, default);
        Assert.Equal(2, host.Transport.Sent.Select(x => x.EventId).Distinct().Count());
        Assert.All(await host.ReadAsync(db => db.Set<DistributedEventOutbox>().ToListAsync()), row => Assert.Equal(DistributedEventOutboxStatus.Sent, row.Status));
    }

    /// <summary>D4：永久失败不安排自动重试，死信也不再阻塞同键。</summary>
    [Fact]
    public async Task A_permanent_failure_becomes_dead_letter_and_releases_the_next_record_of_the_key()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        host.Transport.Results.Enqueue(new() { Status = DistributedEventSendStatus.PermanentFailure, ErrorCode = "message-too-large" });
        await host.InTenantAsync(null, async (sp, _) =>
        {
            var bus = sp.GetRequiredService<IDistributedEventBus>();
            await bus.PublishAsync(new PaidEvent { OrderId = 7 });
            await bus.PublishAsync(new PaidEvent { OrderId = 7 });
        });
        await host.Queue.RunAllAsync(host.Services);
        var rows = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().OrderBy(x => x.Id).ToListAsync());
        Assert.Equal(DistributedEventOutboxStatus.DeadLettered, rows[0].Status);
        Assert.Equal("message-too-large", rows[0].LastErrorCode);
        Assert.Equal(1, rows[0].AttemptCount);
        Assert.Null(rows[0].NextAttemptAt);
        Assert.Equal(DistributedEventOutboxStatus.Sent, rows[1].Status);
    }

    /// <summary>D7：失败重试由兜底扫描接手，预算耗尽成为 Failed。</summary>
    [Fact]
    public async Task The_inbox_scan_retries_lost_immediate_work_and_stops_at_the_attempt_budget()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.MaxAttempts = 2);
        host.Behavior.Throw = true;
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        host.Queue.Drop();
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        var scanner = scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>();
        await scanner.ScanAsync(false, default);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await scanner.ScanAsync(false, default);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(DistributedEventInboxStatus.Failed, row.Status);
        Assert.Equal(2, row.AttemptCount);
        Assert.Null(row.NextAttemptAt);
        Assert.Equal(0, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
    }

    /// <summary>D10 的共享库部分：立即项丢失后也扫描同库租户数据，并恢复该行租户。</summary>
    [Fact]
    public async Task The_scan_includes_shared_database_tenant_rows_after_the_immediate_queue_is_lost()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(DistributedEventTestHost.TenantA, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent()));
        host.Queue.Drop();
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(true, default);
        Assert.Equal(DistributedEventTestHost.TenantA, Assert.Single(host.Transport.Sent).TenantId);
    }

    /// <summary>保留期只删除旧终态；Pending 与最近重放／恢复的终态均保留。</summary>
    [Fact]
    public async Task Retention_keeps_pending_and_recently_modified_records_while_removing_old_terminal_rows()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(null, (_, db) =>
        {
            var old = host.Clock.GetUtcNow().UtcDateTime.AddDays(-20);
            db.AddRange(
                Outbox(old, DistributedEventOutboxStatus.Sent),
                Outbox(old, DistributedEventOutboxStatus.Pending),
                Outbox(host.Clock.GetUtcNow().UtcDateTime, DistributedEventOutboxStatus.DeadLettered));
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var job = ActivatorUtilities.CreateInstance<DistributedEventRetentionJob<EventTestDbContext>>(scope.ServiceProvider);
        await job.ExecuteAsync(new("event-bus.retention", host.Clock.GetUtcNow()), default);
        var rows = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Status == DistributedEventOutboxStatus.Pending);
        Assert.Contains(rows, x => x.Status == DistributedEventOutboxStatus.DeadLettered);
    }

    /// <summary>D10：宿主、共享库两租户与专属库均被扫描；一个库损坏时其余继续且任务失败。</summary>
    [Fact]
    public async Task The_scan_visits_shared_and_dedicated_databases_and_aggregates_database_failures()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        foreach (var tenant in new Guid?[] { null, DistributedEventTestHost.TenantA, DistributedEventTestHost.TenantB, DistributedEventTestHost.DedicatedTenant })
        {
            await host.InTenantAsync(tenant, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent()));
            await using var receiveScope = host.Services.CreateAsyncScope();
            await receiveScope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(tenant), default);
        }
        host.Queue.Drop();
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        await using var scope = host.Services.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>();
        await scanner.ScanAsync(true, default);
        await scanner.ScanAsync(false, default);
        Assert.Equal(4, host.Transport.Sent.Count);
        Assert.Equal(4, host.Transport.Sent.Select(x => x.TenantId).Distinct().Count());
        Assert.Equal(3, await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().CountAsync(x => x.Status == DistributedEventInboxStatus.Processed)));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().CountAsync(x => x.Status == DistributedEventInboxStatus.Processed), dedicated: true));
        host.BreakDedicatedDatabase();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(true, default));
    }

    private static DistributedEventOutbox Outbox(DateTime time, DistributedEventOutboxStatus status) => new()
    {
        EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", OccurredAt = time,
        CreationTime = time, LastModificationTime = time, Status = status
    };
}
