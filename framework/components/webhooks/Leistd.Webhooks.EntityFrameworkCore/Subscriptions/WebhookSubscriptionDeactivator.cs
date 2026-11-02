using Leistd.EventBus.Abstractions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Webhooks.EntityFrameworkCore.Subscriptions;

// 规格"停用的作用"：停用与终结待投递在调用方的同一工作单元里保存；停用事件经可选的事件总线发布，
// 在工作单元内由事件总线推迟到提交后分发。已发出的请求不取消。
internal sealed class WebhookSubscriptionDeactivator<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    TimeProvider timeProvider,
    ILocalEventBus? localEventBus = null)
    where TDbContext : DbContext
{
    // subscription 须由当前工作单元的上下文跟踪；已停用时不重复停用、不重发事件。
    public async Task DisableAsync(WebhookSubscription subscription, WebhookDisabledReason reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (!subscription.IsEnabled)
        {
            return;
        }

        subscription.IsEnabled = false;
        subscription.DisabledReason = reason;
        subscription.DisabledAt = timeProvider.GetUtcNow().UtcDateTime;

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var pending = await dbContext.Set<WebhookDelivery>()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == WebhookDeliveryStatus.Pending)
            .ToListAsync(cancellationToken);

        // 已跟踪的实例保留内存里的新状态：同一事务里刚有结果的那条投递不被覆盖
        foreach (var delivery in pending.Where(x => x.Status == WebhookDeliveryStatus.Pending))
        {
            Terminate(delivery, DescribeDisabled(reason));
        }

        if (localEventBus is not null)
        {
            await localEventBus.PublishAsync(
                new WebhookSubscriptionChangedEvent(subscription.Id, subscription.Name, WebhookSubscriptionChangeKind.Disabled, reason),
                cancellationToken);
        }
    }

    // 把一条待投递记录终结为失败：清空排期，写明原因。
    public static void Terminate(WebhookDelivery delivery, string reason)
    {
        delivery.Status = WebhookDeliveryStatus.Failed;
        delivery.NextAttemptAt = null;
        delivery.NextTrigger = WebhookAttemptTrigger.Automatic;
        delivery.LastError = reason;
    }

    public static string DescribeDisabled(WebhookDisabledReason? reason) => reason switch
    {
        WebhookDisabledReason.EndpointGone => "The subscription was disabled because the endpoint returned 410 Gone.",
        WebhookDisabledReason.ConsecutiveFailures => "The subscription was disabled after failing continuously beyond AutoDisableAfter.",
        _ => "The subscription was disabled manually."
    };

    public const string DeletedReason = "The subscription was deleted.";
}
