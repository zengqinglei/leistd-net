using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Receiving;
using Leistd.EventBus.EntityFrameworkCore.Retention;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>仅发布 Api 将拒收重新解码交给共库、同名传输的接收 Worker。</summary>
public sealed class DistributedEventRedecodeHandoffTests
{
    /// <summary>跨宿主保留原始字节、头和坐标；成功、解码拒绝与重入均可观测。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_writer_hands_off_redecode_and_the_matching_worker_persists_the_outcome(bool failDecode)
    {
        await using var worker = await DistributedEventTestHost.CreateAsync();
        await using var api = await DistributedEventTestHost.CreateAsync(withTransport: false, shareDatabaseWith: worker,
            configure: o => { o.Outbox.DispatchEnabled = o.Inbox.ReceiveEnabled = o.Inbox.ProcessingEnabled = false; });
        var raw = worker.Raw(tenant: DistributedEventTestHost.DedicatedTenant);
        await using var workerScope = worker.Services.CreateAsyncScope();
        await workerScope.ServiceProvider.GetRequiredService<Leistd.EventBus.Distributed.Transport.IDistributedEventReceiver>()
            .RejectAsync(raw, "sdk-bad-message", "Original reason.", default);
        var row = await worker.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        await using var apiScope = api.Services.CreateAsyncScope();
        var management = apiScope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var result = await management.RedecodeAsync(new() { Ids = [row.Id] });
        Assert.Equal(1, result.AffectedCount); Assert.Equal(1, result.DeferredCount); Assert.Empty(result.Skipped);
        Assert.Equal(DistributedEventManagementAction.RedecodeRequested, Assert.Single(api.ManagedEvents).Action);
        Assert.Equal(DistributedEventRejectionStatus.RedecodePending, (await worker.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync())).Status);
        Assert.Equal(1, (await management.GetStatusAsync()).PendingRejections);
        Assert.Equal(0, await worker.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().CountAsync(), dedicated: true));
        Assert.Equal(DistributedEventRecoverySkipReasons.InvalidState,
            Assert.Single((await management.RedecodeAsync(new() { Ids = [row.Id] })).Skipped).ReasonCode);
        var decoded = 0;
        worker.Transport.Decoder = (received, _) =>
        {
            decoded++;
            Assert.Equal(raw.Body.ToArray(), received.Body.ToArray());
            Assert.Equal(raw.Headers, received.Headers); Assert.Equal(raw.Coordinates, received.Coordinates);
            return failDecode ? ValueTask.FromException<Leistd.EventBus.Distributed.Transport.IncomingDistributedEvent>(
                new Leistd.EventBus.Distributed.Transport.DistributedEventDecodeException("sdk-bad-message", "Safe decoder reason."))
                : ValueTask.FromResult(Leistd.EventBus.Distributed.Transport.DistributedEventCodec.Decode(received));
        };
        var job = workerScope.ServiceProvider.GetRequiredService<DistributedEventRedecodeJob<EventTestDbContext>>();
        await job.ExecuteAsync(new("event-bus.redecode", worker.Clock.GetUtcNow()), default);
        await job.ExecuteAsync(new("event-bus.redecode", worker.Clock.GetUtcNow()), default);
        Assert.Equal(1, decoded);
        var final = await worker.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        Assert.Equal(failDecode ? DistributedEventRejectionStatus.Pending : DistributedEventRejectionStatus.Redecoded, final.Status);
        Assert.Equal(1, final.RedecodeCount);
        Assert.Equal(failDecode ? 0 : 1, await worker.ReadAsync(db => db.Set<DistributedEventInbox>().IgnoreQueryFilters().CountAsync(), dedicated: true));
        if (failDecode) { Assert.Equal("Safe decoder reason.", final.Error); Assert.Null(final.ResolvedAt); }
        else Assert.NotNull(final.ResolvedAt);
        Assert.Empty(worker.ManagedEvents);
    }

    /// <summary>错误传输不能领取；关闭接收不执行；保留期不得删除待重新解码。</summary>
    [Fact]
    public async Task Handoff_waits_for_the_matching_enabled_receiver_and_survives_retention()
    {
        await using var worker = await DistributedEventTestHost.CreateAsync();
        var time = worker.Clock.GetUtcNow().UtcDateTime.AddDays(-40);
        await worker.InTenantAsync(null, (_, db) =>
        {
            db.Add(new DistributedEventRejection { Transport = "other-transport", Reason = "bad", Error = "Safe reason.",
                RawBody = worker.Raw().Body.ToArray(), Status = DistributedEventRejectionStatus.RedecodePending,
                ReceivedAt = time, LastModificationTime = time });
            return Task.CompletedTask;
        });
        await using var scope = worker.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<DistributedEventRedecodeJob<EventTestDbContext>>();
        await job.ExecuteAsync(new("event-bus.redecode", worker.Clock.GetUtcNow()), default);
        await scope.ServiceProvider.GetRequiredService<DistributedEventRetentionJob<EventTestDbContext>>()
            .ExecuteAsync(new("event-bus.retention", worker.Clock.GetUtcNow()), default);
        var row = await worker.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        Assert.Equal(DistributedEventRejectionStatus.RedecodePending, row.Status); Assert.Equal(0, row.RedecodeCount);
        var definition = Assert.Single(scope.ServiceProvider.GetServices<RecurringJobDefinition>(), j => j.Name == "event-bus.redecode");
        Assert.True(definition.IsEnabled!(scope.ServiceProvider));
        await using var disabled = await DistributedEventTestHost.CreateAsync(shareDatabaseWith: worker, configure: o => o.Inbox.ReceiveEnabled = false);
        await using var disabledScope = disabled.Services.CreateAsyncScope();
        var disabledDefinition = Assert.Single(disabledScope.ServiceProvider.GetServices<RecurringJobDefinition>(), j => j.Name == "event-bus.redecode");
        Assert.False(disabledDefinition.IsEnabled!(disabledScope.ServiceProvider));
        await disabledScope.ServiceProvider.GetRequiredService<DistributedEventRedecodeJob<EventTestDbContext>>()
            .ExecuteAsync(new("event-bus.redecode", worker.Clock.GetUtcNow()), default);
        Assert.Equal(0, (await worker.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync())).RedecodeCount);
    }
    /// <summary>传输已更名或接收宿主缺席时，勾选及按条件丢弃都是有效出口。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_queued_redecode_can_be_discarded_by_ids_or_filter(bool filter)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var before = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.Add(new DistributedEventRejection { Transport = "renamed-transport", Reason = "bad", Error = "Safe reason.",
                RawBody = host.Raw().Body.ToArray(), Status = DistributedEventRejectionStatus.RedecodePending,
                ReceivedAt = before, LastModificationTime = before });
            return Task.CompletedTask;
        });
        var row = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var result = await service.DiscardAsync(filter ? new() { Filter = new() { Before = before, Reason = "bad" } }
            : new() { Ids = [row.Id] });
        Assert.Equal(1, result.AffectedCount); Assert.Equal(0, result.DeferredCount); Assert.Empty(result.Skipped);
        Assert.Equal(DistributedEventRejectionStatus.Discarded, (await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync())).Status);
        Assert.Equal(0, (await service.GetStatusAsync()).PendingRejections);
        Assert.Equal(DistributedEventManagementAction.Discarded, Assert.Single(host.ManagedEvents).Action);
    }

    /// <summary>专属租户库故障只保留该行待办，同轮的后续健康行仍须完成。</summary>
    [Fact]
    public async Task A_failed_redecode_does_not_block_later_rows_and_keeps_its_pending_request()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.AddRange(new DistributedEventRejection { Transport = "scripted", Reason = "bad", Error = "Safe reason.",
                RawBody = host.Raw(DistributedEventTestHost.DedicatedTenant).Body.ToArray(), Status = DistributedEventRejectionStatus.RedecodePending,
                ReceivedAt = now, LastModificationTime = now },
                new DistributedEventRejection { Transport = "scripted", Reason = "bad", Error = "Safe reason.",
                RawBody = host.Raw().Body.ToArray(), Status = DistributedEventRejectionStatus.RedecodePending,
                ReceivedAt = now, LastModificationTime = now });
            return Task.CompletedTask;
        });
        host.BreakDedicatedDatabase();
        await using var scope = host.Services.CreateAsyncScope();
        var failure = await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<DistributedEventRedecodeJob<EventTestDbContext>>()
            .ExecuteAsync(new("event-bus.redecode", host.Clock.GetUtcNow()), default));
        var rows = await host.ReadAsync(db => db.Set<DistributedEventRejection>().OrderBy(x => x.Id).ToListAsync());
        Assert.Equal(DistributedEventRejectionStatus.Redecoded, rows[1].Status);
        Assert.Equal(DistributedEventRejectionStatus.RedecodePending, rows[0].Status);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventInbox>().CountAsync()));
        Assert.Contains(host.Logs.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Id.Id == 4107 && r.Exception is not null);
    }

    /// <summary>按当前状态丢弃时，不卷入列表之外的另一类未解决记录。</summary>
    [Theory]
    [InlineData(DistributedEventRejectionStatus.Pending)]
    [InlineData(DistributedEventRejectionStatus.RedecodePending)]
    public async Task Discard_filters_preserve_unselected_unresolved_states(DistributedEventRejectionStatus selected)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var before = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            foreach (var state in new[] { DistributedEventRejectionStatus.Pending, DistributedEventRejectionStatus.RedecodePending })
                db.Add(new DistributedEventRejection { Transport = "scripted", Reason = "bad", Error = "Safe reason.",
                    Status = state, ReceivedAt = before, LastModificationTime = before });
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        var result = await service.DiscardAsync(new() { Filter = new() { Before = before, Status = selected } });
        Assert.Equal(1, result.AffectedCount);
        var rows = await host.ReadAsync(db => db.Set<DistributedEventRejection>().OrderBy(x => x.Id).ToListAsync());
        Assert.Single(rows, row => row.Status == DistributedEventRejectionStatus.Discarded);
        Assert.Single(rows, row => row.Status != DistributedEventRejectionStatus.Discarded && row.Status != selected);
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.RedecodeAsync(new()
            { Filter = new() { Before = before, Status = DistributedEventRejectionStatus.RedecodePending } }));
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.DiscardAsync(new()
            { Filter = new() { Before = before, Status = DistributedEventRejectionStatus.Redecoded } }));
    }

}
