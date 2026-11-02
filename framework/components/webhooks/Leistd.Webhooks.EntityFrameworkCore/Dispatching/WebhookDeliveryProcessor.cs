using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;
using Leistd.Webhooks.EntityFrameworkCore.Subscriptions;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore.Dispatching;

// 规格"投递链路"第 4、5 步，立即投递与兜底扫描共用：在投递所属租户的上下文里处理一条投递。
// 读取与写回各用一个独立工作单元，HTTP 请求夹在两者之间发出，不在请求期间占着事务与连接。
internal sealed class WebhookDeliveryProcessor<TDbContext>(
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    IWebhookSender sender,
    WebhookAttemptClassifier classifier,
    WebhookSecretProtector secretProtector,
    WebhookSubscriptionDeactivator<TDbContext> deactivator,
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider timeProvider)
    where TDbContext : DbContext
{
    // expectedAttemptCount：队列项入队时的尝试次数，与当前不符说明扫描已经处理过，跳过；扫描传 null。
    public async Task ProcessAsync(Guid? tenantId, Guid deliveryId, int? expectedAttemptCount, CancellationToken cancellationToken)
    {
        // 同层时不切：切换会丢掉当前上下文里的租户名
        using (tenantId == currentTenant.Id ? null : currentTenant.Change(tenantId))
        {
            var request = await PrepareAsync(deliveryId, expectedAttemptCount, cancellationToken);
            if (request is null)
            {
                return;
            }

            var startedAt = timeProvider.GetUtcNow();
            var result = await sender.SendAsync(request.Value.Request with { Timestamp = startedAt }, cancellationToken);
            await RecordAsync(deliveryId, request.Value.Trigger, startedAt.UtcDateTime, result, cancellationToken);
        }
    }

    // 重读投递与订阅：订阅已停用或已删除时按"停用的作用"终结这条投递，不发请求。
    private async Task<(WebhookSendRequest Request, WebhookAttemptTrigger Trigger)?> PrepareAsync(
        Guid deliveryId,
        int? expectedAttemptCount,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        var delivery = await dbContext.Set<WebhookDelivery>().FirstOrDefaultAsync(x => x.Id == deliveryId, cancellationToken);
        if (delivery is not { Status: WebhookDeliveryStatus.Pending }
            || (expectedAttemptCount is { } expected && delivery.AttemptCount != expected))
        {
            return null;
        }

        var subscription = await dbContext.Set<WebhookSubscription>()
            .FirstOrDefaultAsync(x => x.Id == delivery.SubscriptionId, cancellationToken);
        if (subscription is not { IsEnabled: true })
        {
            WebhookSubscriptionDeactivator<TDbContext>.Terminate(
                delivery,
                subscription is null
                    ? WebhookSubscriptionDeactivator<TDbContext>.DeletedReason
                    : WebhookSubscriptionDeactivator<TDbContext>.DescribeDisabled(subscription.DisabledReason));
            await unitOfWork.CompleteAsync(cancellationToken);
            return null;
        }

        var webhookEvent = await dbContext.Set<WebhookEvent>().AsNoTracking()
            .FirstAsync(x => x.Id == delivery.EventId, cancellationToken);
        var secrets = secretProtector.GetSigningSecrets(subscription, timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.CompleteAsync(cancellationToken);

        var request = new WebhookSendRequest
        {
            EventId = webhookEvent.Id,
            EventType = webhookEvent.Type,
            SubscriptionId = subscription.Id,
            Url = new Uri(subscription.Url, UriKind.Absolute),
            // 实际签名时刻在发送前取
            Timestamp = default,
            Body = webhookEvent.Payload,
            Headers = subscription.Headers,
            Secrets = secrets
        };
        return (request, delivery.NextTrigger);
    }

    // 写尝试记录，并按分类结果更新投递与订阅（规格"结果"顺序表与连续失败停用）。
    private async Task RecordAsync(
        Guid deliveryId,
        WebhookAttemptTrigger trigger,
        DateTime startedAt,
        WebhookSendResult result,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 请求期间投递被删除（订阅被删）时没有可记的地方
        var delivery = await dbContext.Set<WebhookDelivery>().FirstOrDefaultAsync(x => x.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        dbContext.Set<WebhookAttempt>().Add(new WebhookAttempt
        {
            DeliveryId = delivery.Id,
            AttemptedAt = startedAt,
            Trigger = trigger,
            RequestHeaders = new Dictionary<string, string>(result.RequestHeaders, StringComparer.OrdinalIgnoreCase),
            StatusCode = result.StatusCode,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            Response = result.Response,
            Error = Truncate(result.Error, WebhookAttemptConfiguration.MaxErrorLength)
        });

        delivery.AttemptCount++;
        if (trigger == WebhookAttemptTrigger.Automatic)
        {
            delivery.AutoAttemptCount++;
        }

        delivery.LastAttemptAt = startedAt;
        delivery.LastStatusCode = result.StatusCode;
        delivery.LastError = Truncate(result.Error, WebhookDeliveryConfiguration.MaxLastErrorLength);

        var outcome = classifier.Classify(result, trigger, delivery.AutoAttemptCount, now);

        // 请求期间订阅被停用、投递已被终结时只补记这次尝试，不改终态
        if (delivery.Status == WebhookDeliveryStatus.Pending)
        {
            delivery.Status = outcome.Status;
            delivery.NextAttemptAt = outcome.NextAttemptAt;
            delivery.NextTrigger = WebhookAttemptTrigger.Automatic;
        }

        var subscription = await dbContext.Set<WebhookSubscription>()
            .FirstOrDefaultAsync(x => x.Id == delivery.SubscriptionId, cancellationToken);
        if (subscription is not null)
        {
            await UpdateSubscriptionAsync(subscription, result, outcome, now, cancellationToken);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
    }

    private async Task UpdateSubscriptionAsync(
        WebhookSubscription subscription,
        WebhookSendResult result,
        WebhookAttemptOutcome outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            subscription.FirstFailureAt = null;
            return;
        }

        subscription.FirstFailureAt ??= now;
        if (outcome.DisablesSubscription)
        {
            await deactivator.DisableAsync(subscription, WebhookDisabledReason.EndpointGone, cancellationToken);
        }
        else if (now - subscription.FirstFailureAt.Value > options.CurrentValue.AutoDisableAfter)
        {
            await deactivator.DisableAsync(subscription, WebhookDisabledReason.ConsecutiveFailures, cancellationToken);
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => value is { Length: var length } && length > maxLength ? value[..maxLength] : value;
}
