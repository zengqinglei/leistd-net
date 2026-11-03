using System.Text.Json;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Webhooks.EntityFrameworkCore.Publishing;

// 规格"投递链路"第 1 步：在调用方的工作单元里写一条事件与每个匹配订阅的投递，提交后立即入队。
// 订阅按当前租户隔离由 IMultiTenant 的全局查询过滤器承担；写入的 TenantId 取当前租户，与过滤口径一致。
internal sealed class EfCoreWebhookPublisher<TDbContext>(
    IWebhookDefinitionManager definitionManager,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    ICurrentTenant currentTenant,
    WebhookDeliveryQueue<TDbContext> deliveryQueue,
    TimeProvider timeProvider) : IWebhookPublisher
    where TDbContext : DbContext
{
    // 新投递留给立即投递的宽限期，兜底扫描在此之后才接手
    internal static readonly TimeSpan ImmediateDispatchGrace = TimeSpan.FromMinutes(1);

    public async Task PublishAsync(string eventType, object? data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        if (definitionManager.GetOrNull(eventType) is null)
        {
            throw new ArgumentException(
                $"Webhook event type '{eventType}' is not defined; declare it in an IWebhookDefinitionProvider.", nameof(eventType));
        }

        if (unitOfWorkManager.Current is null)
        {
            throw new InvalidOperationException(
                "Webhook events can only be published inside an active unit of work, so that they commit or roll back "
                + "together with the business data. In a local event handler, declare "
                + "[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)].");
        }

        var now = timeProvider.GetUtcNow();
        var tenantId = currentTenant.Id;
        var webhookEvent = CreateEvent(eventType, data, tenantId, now);

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscriptionIds = await dbContext.Set<WebhookSubscription>()
            .AsNoTracking()
            .Where(x => x.IsEnabled
                        && (x.EventTypes.Contains(eventType) || x.EventTypes.Contains(WebhookDefinition.AllEventTypes)))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var deliveries = subscriptionIds.Select(subscriptionId => CreateDelivery(webhookEvent, subscriptionId, now.UtcDateTime)).ToList();

        dbContext.Set<WebhookEvent>().Add(webhookEvent);
        dbContext.Set<WebhookDelivery>().AddRange(deliveries);
        deliveryQueue.EnqueueAfterCommit(deliveries);
    }

    // 事件信封 {type, timestamp, data}，System.Text.Json Web 默认（camelCase），发布时冻结：之后修改 data 不影响已存载荷。
    // 管理用例发测试事件时也用它，只投给指定的订阅。
    internal static WebhookEvent CreateEvent(string eventType, object? data, Guid? tenantId, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        Type = eventType,
        Payload = JsonSerializer.Serialize(new { type = eventType, timestamp = now, data }, JsonSerializerOptions.Web),
        OccurredAt = now.UtcDateTime
    };

    // 新投递：待投递、自动触发，留宽限期给立即投递。
    internal static WebhookDelivery CreateDelivery(WebhookEvent webhookEvent, Guid subscriptionId, DateTime now) => new()
    {
        TenantId = webhookEvent.TenantId,
        EventId = webhookEvent.Id,
        SubscriptionId = subscriptionId,
        Status = WebhookDeliveryStatus.Pending,
        NextTrigger = WebhookAttemptTrigger.Automatic,
        NextAttemptAt = now + ImmediateDispatchGrace,
        CreationTime = now
    };
}
