using Leistd.AmbientContext;
using Leistd.BackgroundJobs.Queues;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

// 自增 ID 只在提交后读取；不能在 PublishAsync 的 Add 时捕获尚未生成的 0。
internal sealed class DistributedEventQueue<TDbContext>(
    IUnitOfWorkManager units,
    IBackgroundTaskQueue queue,
    IOptionsMonitor<DistributedEventOptions> options,
    ILogger<DistributedEventQueue<TDbContext>> logger) where TDbContext : DbContext
{
    public void OutboxAfterCommit(DistributedEventOutbox record)
    {
        if (!options.CurrentValue.Outbox.DispatchEnabled) return;
        AfterCommit(() => Enqueue(record.TenantId, record.Id, record.AttemptCount, outbox: true));
    }

    public void InboxAfterCommit(DistributedEventInbox record)
    {
        if (!options.CurrentValue.Inbox.ProcessingEnabled) return;
        AfterCommit(() => Enqueue(record.TenantId, record.Id, record.AttemptCount, outbox: false));
    }

    private void AfterCommit(Action action)
    {
        var unit = units.Current ?? throw new InvalidOperationException("Immediate dispatch requires an active unit of work.");
        unit.Disposed += (_, args) => { if (args.UnitOfWork.IsCompleted) action(); };
    }

    private void Enqueue(Guid? tenantId, long id, int attempts, bool outbox)
    {
        if (!queue.TryQueue(async (services, ct) =>
        {
            var ambient = services.GetService<IAmbientContext>();
            using var identity = ambient?.Restore(ambient.Capture() with { Principal = null });
            if (outbox)
                await services.GetRequiredService<DistributedEventOutboxProcessor<TDbContext>>().ProcessAsync(tenantId, id, attempts, ct);
            else
                await services.GetRequiredService<DistributedEventInboxProcessor<TDbContext>>().ProcessAsync(tenantId, id, attempts, ct);
        }))
            logger.LogWarning("The background queue is full; distributed event record {RecordId} will be handled by the scan.", id);
    }
}
