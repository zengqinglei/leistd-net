using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>管理用例直接入口、按条件恢复、目录与租户范围。</summary>
public sealed class DistributedEventManagementTests
{
    /// <summary>D16：1,200 条按条件分成三批提交，迟到记录不卷入，不灌进立即队列。</summary>
    [Fact]
    public async Task Filter_redelivery_commits_all_eligible_records_in_batches_with_accurate_audit_and_no_queue_flood()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var before = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.AddRange(Enumerable.Range(0, 1200).Select(_ => DeadLetter(before)));
            db.Add(DeadLetter(before.AddSeconds(1)));
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var result = await service.RedeliverAsync(new() { Filter = new() { Before = before, ErrorCode = "offline" } });
        Assert.Equal(1200, result.AffectedCount);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, host.Queue.Count);
        var rows = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().ToListAsync());
        Assert.Equal(1200, rows.Count(x => x.Status == DistributedEventOutboxStatus.Pending));
        Assert.All(rows.Where(x => x.Status == DistributedEventOutboxStatus.Pending), row =>
        { Assert.Null(row.FirstFailureAt); Assert.Equal(38, row.AttemptCount); Assert.Equal(1, row.RedeliverCount); });
        Assert.Equal(new long[] { 500, 500, 200 }, host.ManagedEvents.Select(x => x.AffectedCount));
        Assert.All(host.ManagedEvents, audit =>
        {
            Assert.Equal(DistributedEventManagementAction.Redelivered, audit.Action);
            Assert.Null(audit.Ids);
            Assert.Equal(before, JsonDocument.Parse(audit.FilterJson!).RootElement.GetProperty("before").GetDateTime());
        });
    }

    /// <summary>D18 的共享库部分：宿主见全库、租户只见自己；单条重投在行所属租户下提交。</summary>
    [Fact]
    public async Task Host_reads_all_shared_rows_but_tenant_queries_and_recovery_remain_tenant_scoped()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(null, (_, db) => { db.Add(DeadLetter(host.Clock.GetUtcNow().UtcDateTime)); return Task.CompletedTask; });
        await host.InTenantAsync(DistributedEventTestHost.TenantA, (_, db) =>
        { var row = DeadLetter(host.Clock.GetUtcNow().UtcDateTime); row.TenantId = DistributedEventTestHost.TenantA; db.Add(row); return Task.CompletedTask; });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        Assert.Equal(2, (await service.GetOutboxListAsync(new())).TotalCount);
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.TenantA))
        {
            var page = await service.GetOutboxListAsync(new());
            Assert.Equal(DistributedEventTestHost.TenantA, Assert.Single(page.Items).TenantId);
            Assert.Equal(1, (await service.RedeliverAsync(new() { Ids = [page.Items.Single().Id] })).AffectedCount);
            var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetStatusAsync("host"));
            Assert.Equal(DistributedEventErrorCodes.HostOnly, ex.Code);
            Assert.Equal(DistributedEventErrorCodes.HostOnly, (await Assert.ThrowsAsync<BusinessException>(() => service.GetRejectionsAsync(new()))).Code);
            Assert.Null((await service.GetStatusAsync()).PendingRejections);
        }
        Assert.Equal(DistributedEventTestHost.TenantA, Assert.Single(host.ManagedEventTenants));
        Assert.Contains(await service.GetDatabasesAsync(), x => x.Key == "host" && x.IsHost);
    }

    /// <summary>D17：目录合并订阅与历史并聚合计数；读取列表与详情概要不包含载荷。</summary>
    [Fact]
    public async Task Catalog_combines_subscriptions_and_history_and_list_dtos_do_not_expose_payload()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(null, (_, db) =>
        { var row = DeadLetter(host.Clock.GetUtcNow().UtcDateTime); row.EventName = "invoice.issued"; db.Add(row); return Task.CompletedTask; });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var catalog = await service.GetCatalogAsync();
        Assert.True(catalog.Single(x => x.EventName == "order.paid").Incoming);
        var invoice = catalog.Single(x => x.EventName == "invoice.issued");
        Assert.True(invoice.Outgoing); Assert.False(invoice.Incoming); Assert.Equal(1, invoice.DeadLettered); Assert.Equal("offline", invoice.LastErrorCode);
        var page = await service.GetOutboxListAsync(new());
        Assert.DoesNotContain("payload", JsonSerializer.Serialize(page, JsonSerializerOptions.Web), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{}", (await service.GetOutboxPayloadAsync(page.Items.Single().Id)).Payload);
    }

    /// <summary>D19 与直接用例校验：禁用测试事件、ids/filter 互斥、筛选截止与状态均不能绕过。</summary>
    [Fact]
    public async Task Management_validation_applies_outside_http_and_test_disabled_is_a_business_error()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.TestEvent.Enabled = false);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        Assert.False((await service.GetStatusAsync()).TestEventEnabled);
        Assert.DoesNotContain(await service.GetCatalogAsync(), x => x.EventName == DistributedEventTestEvent.Name);
        Assert.Equal(DistributedEventErrorCodes.TestEventDisabled, (await Assert.ThrowsAsync<BusinessException>(() => service.SendTestEventAsync())).Code);
        await Assert.ThrowsAsync<ValidationException>(() => service.RedeliverAsync(new()));
        await Assert.ThrowsAsync<ValidationException>(() => service.RedeliverAsync(new() { Ids = [1], Filter = new() { Before = host.Clock.GetUtcNow().UtcDateTime } }));
        await Assert.ThrowsAsync<ValidationException>(() => service.RedeliverAsync(new() { Filter = new() }));
        await Assert.ThrowsAsync<ValidationException>(() => service.ReplayAsync(new() { Filter = new() { Before = host.Clock.GetUtcNow().UtcDateTime, Status = DistributedEventInboxStatus.Pending } }));
    }

    /// <summary>Processed 重放清零预算与完成时刻，保留事件 ID；处理器再次执行。</summary>
    [Fact]
    public async Task Replaying_a_processed_record_resets_the_round_and_executes_the_handler_again()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        await host.Queue.RunAllAsync(host.Services);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var result = await service.ReplayAsync(new() { Ids = [row.Id] });
        Assert.Equal(1, result.AffectedCount);
        var pending = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(0, pending.AttemptCount); Assert.Null(pending.ProcessedAt); Assert.Equal(row.EventId, pending.EventId);
        Assert.Equal(1, pending.ReplayCount);
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(2, (await host.ReadAsync(db => db.Set<EventProjection>().SingleAsync())).Count);
    }

    /// <summary>D14：原消息经修复后的解码器进入收件箱；失败重解码不新增拒收。</summary>
    [Fact]
    public async Task Redecoding_uses_the_original_bytes_and_does_not_create_another_rejection_on_failure()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        var raw = host.Raw();
        await receiver.RejectAsync(raw, "sdk-decode-failed", "The decoder was unavailable.", default);
        var row = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        Assert.Equal(1, (await service.RedecodeAsync(new() { Ids = [row.Id] })).AffectedCount);
        Assert.Equal(DistributedEventRejectionStatus.Redecoded, (await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync())).Status);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));
        await receiver.RejectAsync(raw with { Body = new byte[] { 1, 2 } }, "sdk-decode-failed", "Bad bytes.", default);
        var bad = await host.ReadAsync(db => db.Set<DistributedEventRejection>().OrderByDescending(x => x.Id).FirstAsync());
        var failed = await service.RedecodeAsync(new() { Ids = [bad.Id] });
        Assert.Equal(0, failed.AffectedCount);
        Assert.Equal(DistributedEventRecoverySkipReasons.RedecodeFailed, Assert.Single(failed.Skipped).ReasonCode);
        Assert.Equal(2, await host.ReadAsync(db => db.Set<DistributedEventRejection>().CountAsync()));
        Assert.Equal(DistributedEventRejectionStatus.Pending, (await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync(x => x.Id == bad.Id))).Status);
    }

    /// <summary>D15：只写进程没有传输也能持久发布并读取状态，不由可选依赖推断分工。</summary>
    [Fact]
    public async Task An_explicit_writer_only_process_reports_unknown_transport_and_can_still_publish()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: false, configure: o =>
        { o.Outbox.DispatchEnabled = false; o.Inbox.ReceiveEnabled = false; o.Inbox.ProcessingEnabled = false; });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        await service.SendTestEventAsync();
        var status = await service.GetStatusAsync();
        Assert.Null(status.TransportName); Assert.Equal("unknown", status.TransportStatus);
        Assert.Equal(1, status.PendingOutbox); Assert.Equal(0, host.Queue.Count);
    }

    /// <summary>D18：宿主显式选独立库，读取该库并在原租户上下文提交恢复及发送。</summary>
    [Fact]
    public async Task A_host_selected_dedicated_database_is_read_and_recovered_using_the_record_tenant()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await host.InTenantAsync(null, (_, db) => { db.Add(DeadLetter(host.Clock.GetUtcNow().UtcDateTime)); return Task.CompletedTask; });
        await host.InTenantAsync(DistributedEventTestHost.DedicatedTenant, (_, db) =>
        { var row = DeadLetter(host.Clock.GetUtcNow().UtcDateTime); row.TenantId = DistributedEventTestHost.DedicatedTenant; db.Add(row); return Task.CompletedTask; });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var database = DistributedEventTestHost.DedicatedTenant.ToString();
        var page = await service.GetOutboxListAsync(new() { Database = database });
        var record = Assert.Single(page.Items);
        Assert.Equal(DistributedEventTestHost.DedicatedTenant, record.TenantId);
        Assert.Equal(1, (await service.RedeliverAsync(new() { Ids = [record.Id] }, database)).AffectedCount);
        Assert.Equal(DistributedEventTestHost.DedicatedTenant, Assert.Single(host.ManagedEventTenants));
        await host.Queue.RunAllAsync(host.Services);
        Assert.Equal(DistributedEventTestHost.DedicatedTenant, Assert.Single(host.Transport.Sent).TenantId);
        Assert.Equal(1, (await service.GetStatusAsync()).DeadLettered);
        Assert.Equal(0, (await service.GetStatusAsync(database)).DeadLettered);
    }

    private static DistributedEventOutbox DeadLetter(DateTime time) => new()
    {
        EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", OccurredAt = time,
        CreationTime = time, LastModificationTime = time, FirstFailureAt = time.AddDays(-2),
        Status = DistributedEventOutboxStatus.DeadLettered, AttemptCount = 38, LastErrorCode = "offline"
    };
}
