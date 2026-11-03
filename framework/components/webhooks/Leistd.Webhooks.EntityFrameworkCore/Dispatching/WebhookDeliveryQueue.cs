using Leistd.AmbientContext;
using Leistd.BackgroundJobs.Queues;
using Leistd.UnitOfWork;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.Webhooks.EntityFrameworkCore.Dispatching;

// 规格"投递链路"第 2 步：工作单元提交后把投递送进进程内队列立即发送。
// 回滚、未完成即释放时不入队；队列满或进程退出时丢失的项由兜底扫描按 NextAttemptAt 补发。
internal sealed class WebhookDeliveryQueue<TDbContext>(
    IUnitOfWorkManager unitOfWorkManager,
    IBackgroundTaskQueue queue,
    TimeProvider timeProvider,
    ILogger<WebhookDeliveryQueue<TDbContext>> logger)
    where TDbContext : DbContext
{
    // 投递须已加入当前工作单元；记下此刻的尝试次数，队列项执行时据此跳过已被扫描处理过的投递。
    public void EnqueueAfterCommit(IReadOnlyCollection<WebhookDelivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        var unitOfWork = RequireUnitOfWork();
        if (deliveries.Count == 0)
        {
            return;
        }

        var items = deliveries.Select(x => new QueuedDelivery(x.TenantId, x.Id, x.AttemptCount)).ToArray();

        // 子工作单元的 Disposed 委托给最外层：只有真正提交的那次释放才入队
        unitOfWork.Disposed += (_, args) =>
        {
            if (args.UnitOfWork.IsCompleted)
            {
                Enqueue(items);
            }
        };
    }

    // 规格"投递链路"第 6 步：手动重发与排期同事务保存，提交后立即入队；队列丢失时扫描照样按 NextTrigger 记为手动。
    // 只接受终态投递，是否允许重发（订阅已停用等）由调用方先判断。
    public void ScheduleManualRetry(WebhookDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.Status == WebhookDeliveryStatus.Pending)
        {
            throw new InvalidOperationException($"Webhook delivery '{delivery.Id}' is still pending and cannot be retried manually.");
        }

        RequireUnitOfWork();
        delivery.Status = WebhookDeliveryStatus.Pending;
        delivery.NextTrigger = WebhookAttemptTrigger.Manual;
        delivery.NextAttemptAt = timeProvider.GetUtcNow().UtcDateTime;
        EnqueueAfterCommit([delivery]);
    }

    private IUnitOfWork RequireUnitOfWork()
        => unitOfWorkManager.Current
           ?? throw new InvalidOperationException(
               "Webhook deliveries can only be scheduled inside an active unit of work; "
               + "they are written together with the caller's data and dispatched after it commits.");

    private void Enqueue(QueuedDelivery[] items)
    {
        foreach (var item in items)
        {
            if (!queue.TryQueue((services, cancellationToken) => ProcessAsSystemAsync(services, item, cancellationToken)))
            {
                logger.LogWarning(
                    "The background queue is full; webhook delivery {DeliveryId} will be sent by the dispatch scan instead.",
                    item.DeliveryId);
            }
        }
    }

    // 投递在系统身份下执行，不继承入队请求的用户。后台队列在执行时还原入队时的主体、租户与链路标识；
    // 投递处理是系统行为，与兜底扫描一致，只保留租户与链路标识，主体还原为匿名，自动停用等写操作因此不记到发起请求的用户名下。
    // 未注册 IAmbientContext 时照原样执行：没有环境上下文机制，也就没有可继承的主体。
    private static async ValueTask ProcessAsSystemAsync(IServiceProvider services, QueuedDelivery item, CancellationToken cancellationToken)
    {
        var ambientContext = services.GetService<IAmbientContext>();
        using var systemIdentity = ambientContext?.Restore(ambientContext.Capture() with { Principal = null });
        await services.GetRequiredService<WebhookDeliveryProcessor<TDbContext>>()
            .ProcessAsync(item.TenantId, item.DeliveryId, item.ExpectedAttemptCount, cancellationToken);
    }

    private readonly record struct QueuedDelivery(Guid? TenantId, Guid DeliveryId, int ExpectedAttemptCount);
}
