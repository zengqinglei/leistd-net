using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>一分钟宽限只留给本进程的立即路径；没有立即路径的新行由下一轮扫描直接取走。</summary>
public sealed class DistributedEventGracePeriodTests
{
    /// <summary>只写发件箱的进程发布的行在提交后立即可扫描，同库 Worker 的下一轮扫描即发送。</summary>
    [Fact]
    public async Task A_writer_publication_is_due_at_once_and_the_next_worker_scan_sends_it()
    {
        await using var worker = await DistributedEventTestHost.CreateAsync();
        await using var api = await DistributedEventTestHost.CreateAsync(withTransport: false, shareDatabaseWith: worker,
            configure: o => { o.Outbox.DispatchEnabled = o.Inbox.ReceiveEnabled = o.Inbox.ProcessingEnabled = false; });
        var now = api.Clock.GetUtcNow().UtcDateTime;
        await api.InTenantAsync(null, (sp, _) => sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent { OrderId = 7 }));
        Assert.Equal(now, (await worker.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).NextAttemptAt);

        await using var scope = worker.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(true, default);
        Assert.Single(worker.Transport.Sent);
        Assert.Equal(DistributedEventOutboxStatus.Sent, (await worker.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync())).Status);
    }

    /// <summary>只接收不处理的进程写入的收件箱行立即可扫描，处理进程的下一轮扫描即处理。</summary>
    [Fact]
    public async Task A_receive_only_inbox_row_is_due_at_once_and_the_next_processing_scan_handles_it()
    {
        await using var processor = await DistributedEventTestHost.CreateAsync();
        await using var receiver = await DistributedEventTestHost.CreateAsync(shareDatabaseWith: processor, configure: o => o.Inbox.ProcessingEnabled = false);
        var now = receiver.Clock.GetUtcNow().UtcDateTime;
        await using (var receive = receiver.Services.CreateAsyncScope())
            await receive.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(receiver.Raw(), default);
        Assert.Equal(0, receiver.Queue.Count);
        Assert.Equal(now, (await processor.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).NextAttemptAt);

        await using var scope = processor.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(false, default);
        Assert.Equal(DistributedEventInboxStatus.Processed, (await processor.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).Status);
        Assert.Equal(1, await processor.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
    }

    /// <summary>批量重新解码不入立即队列，新行立即可扫描；单条重新解码入队并保留宽限。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redecoded_inbox_rows_keep_the_grace_only_when_queued_for_immediate_processing(bool single)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await host.InTenantAsync(null, (_, db) =>
        {
            db.Add(new DistributedEventRejection { Transport = "scripted", Reason = "sdk-decoder-missing", Error = "Decoder was unavailable.",
                RawBody = host.Raw().Body.ToArray(), ReceivedAt = now, LastModificationTime = now });
            return Task.CompletedTask;
        });
        var rejection = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().RedecodeAsync(single
            ? new() { Ids = [rejection.Id] } : new() { Filter = new() { Before = now, Reason = "sdk-decoder-missing" } });
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(single ? now.AddMinutes(1) : now, (await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).NextAttemptAt);
        Assert.Equal(single ? 1 : 0, host.Queue.Count);
        if (single) return;

        await scope.ServiceProvider.GetRequiredService<DistributedEventScanner<EventTestDbContext>>().ScanAsync(false, default);
        Assert.Equal(DistributedEventInboxStatus.Processed, (await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).Status);
    }
}
