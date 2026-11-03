using Leistd.Data.Paging;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Publishing;
using Leistd.Webhooks.EntityFrameworkCore.Subscriptions;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Stores;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Webhooks.EntityFrameworkCore.Stores;

// 管理用例的 EF 存储。租户隔离由 IMultiTenant 的全局查询过滤器承担：找不到即 null，不区分"别的租户的"。
// 写入只改当前工作单元的上下文，由用例保存；停用、手动排期与提交后入队沿用投递链路的内部服务，不另写一份。
internal sealed class EfCoreWebhookManagementStore<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    ICurrentTenant currentTenant,
    WebhookSubscriptionDeactivator<TDbContext> deactivator,
    WebhookDeliveryQueue<TDbContext> deliveryQueue,
    TimeProvider timeProvider) : IWebhookManagementStore
    where TDbContext : DbContext
{
    public async Task<PagedResult<WebhookSubscriptionEntry>> GetSubscriptionsAsync(
        GetWebhookSubscriptionsInputDto input,
        CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var query = dbContext.Set<WebhookSubscription>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim().ToLowerInvariant();
            query = query.Where(x => x.Name.ToLower().Contains(keyword) || x.Url.ToLower().Contains(keyword));
        }

        if (input.IsEnabled is { } isEnabled)
        {
            query = query.Where(x => x.IsEnabled == isEnabled);
        }

        if (!string.IsNullOrWhiteSpace(input.EventType))
        {
            // 订阅了全部事件的也匹配任何类型，与发布时的匹配口径一致
            var eventType = input.EventType;
            query = query.Where(x => x.EventTypes.Contains(eventType) || x.EventTypes.Contains(WebhookDefinition.AllEventTypes));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var subscriptions = await query
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id)
            .Skip(input.Offset)
            .Take(input.Limit)
            .ToListAsync(cancellationToken);

        return new PagedResult<WebhookSubscriptionEntry>(totalCount, subscriptions.Select(ToEntry));
    }

    public async Task<WebhookSubscriptionEntry?> FindSubscriptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = await dbContext.Set<WebhookSubscription>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return subscription is null ? null : ToEntry(subscription);
    }

    // 对这一组订阅一次分组查询，不逐行查。
    public async Task<IReadOnlyDictionary<Guid, WebhookDeliverySummary>> GetDeliverySummariesAsync(
        IReadOnlyCollection<Guid> subscriptionIds,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var ids = subscriptionIds.ToList();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var summaries = await dbContext.Set<WebhookDelivery>().AsNoTracking()
            .Where(x => ids.Contains(x.SubscriptionId))
            .GroupBy(x => x.SubscriptionId)
            .Select(group => new
            {
                SubscriptionId = group.Key,
                Succeeded = group.Count(x => x.CreationTime >= since && x.Status == WebhookDeliveryStatus.Succeeded),
                Failed = group.Count(x => x.CreationTime >= since && x.Status == WebhookDeliveryStatus.Failed),
                Last = group
                    .OrderByDescending(x => x.CreationTime)
                    .ThenByDescending(x => x.Id)
                    .Select(x => new WebhookSubscriptionLastDeliveryDto
                    {
                        Id = x.Id,
                        Status = x.Status,
                        LastStatusCode = x.LastStatusCode,
                        CreationTime = x.CreationTime
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return summaries.ToDictionary(x => x.SubscriptionId, x => new WebhookDeliverySummary(x.Succeeded, x.Failed, x.Last));
    }

    public async Task<WebhookSubscriptionEntry> AddSubscriptionAsync(
        WebhookSubscriptionFields fields,
        string protectedSecret,
        DateTime creationTime,
        CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = new WebhookSubscription
        {
            // 与过滤口径一致：写入时显式取当前租户，不依赖宿主上下文代填
            TenantId = currentTenant.Id,
            Secret = protectedSecret,
            CreationTime = creationTime
        };
        Apply(subscription, fields);
        dbContext.Set<WebhookSubscription>().Add(subscription);
        return ToEntry(subscription);
    }

    public Task<WebhookSubscriptionEntry?> UpdateSubscriptionAsync(Guid id, WebhookSubscriptionFields fields, CancellationToken cancellationToken)
        => ModifyAsync(id, subscription => Apply(subscription, fields), cancellationToken);

    public async Task<(WebhookSubscriptionEntry Subscription, bool Changed)?> EnableSubscriptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await FindTrackedAsync(id, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var changed = !subscription.IsEnabled;
        if (changed)
        {
            subscription.IsEnabled = true;
            subscription.DisabledReason = null;
            subscription.DisabledAt = null;
            subscription.FirstFailureAt = null;
        }

        return (ToEntry(subscription), changed);
    }

    public async Task<(WebhookSubscriptionEntry Subscription, bool Changed)?> DisableSubscriptionAsync(
        Guid id,
        WebhookDisabledReason reason,
        CancellationToken cancellationToken)
    {
        var subscription = await FindTrackedAsync(id, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var changed = await deactivator.DisableAsync(subscription, reason, cancellationToken);
        return (ToEntry(subscription), changed);
    }

    public Task<WebhookSubscriptionEntry?> RotateSecretAsync(
        Guid id,
        string protectedSecret,
        DateTime? previousSecretExpiresAt,
        CancellationToken cancellationToken)
        => ModifyAsync(id, subscription =>
        {
            subscription.PreviousSecret = previousSecretExpiresAt is null ? null : subscription.Secret;
            subscription.PreviousSecretExpiresAt = previousSecretExpiresAt;
            subscription.Secret = protectedSecret;
        }, cancellationToken);

    public async Task<WebhookSubscriptionEntry?> DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await FindTrackedAsync(id, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        // 加载后 RemoveRange，不用 ExecuteDelete：InMemory 不支持，且要经过上下文上的拦截器；不依赖库里的级联
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var deliveries = await dbContext.Set<WebhookDelivery>().Where(x => x.SubscriptionId == id).ToListAsync(cancellationToken);
        var deliveryIds = deliveries.Select(x => x.Id).ToList();
        var attempts = await dbContext.Set<WebhookAttempt>().Where(x => deliveryIds.Contains(x.DeliveryId)).ToListAsync(cancellationToken);

        dbContext.Set<WebhookAttempt>().RemoveRange(attempts);
        dbContext.Set<WebhookDelivery>().RemoveRange(deliveries);
        dbContext.Set<WebhookSubscription>().Remove(subscription);
        return ToEntry(subscription);
    }

    public async Task<int> RetryFailedDeliveriesAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var failed = await dbContext.Set<WebhookDelivery>()
            .Where(x => x.SubscriptionId == subscriptionId && x.Status == WebhookDeliveryStatus.Failed)
            .ToListAsync(cancellationToken);
        foreach (var delivery in failed)
        {
            deliveryQueue.ScheduleManualRetry(delivery);
        }

        return failed.Count;
    }

    public async Task<WebhookDeliveryEntry> AddDeliveryAsync(
        WebhookSubscriptionEntry subscription,
        string eventType,
        object? data,
        CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var webhookEvent = EfCoreWebhookPublisher<TDbContext>.CreateEvent(eventType, data, subscription.TenantId, now);
        var delivery = EfCoreWebhookPublisher<TDbContext>.CreateDelivery(webhookEvent, subscription.Id, now.UtcDateTime);
        dbContext.Set<WebhookEvent>().Add(webhookEvent);
        dbContext.Set<WebhookDelivery>().Add(delivery);
        deliveryQueue.EnqueueAfterCommit([delivery]);
        return ToEntry(delivery, webhookEvent.Type, subscription.Name, subscription.Url, subscription.IsEnabled);
    }

    public async Task<PagedResult<WebhookDeliveryEntry>> GetDeliveriesAsync(GetWebhookDeliveriesInputDto input, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var query = DeliveryRows(dbContext);
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            // 事件标识按整值匹配（Guid 的文本形式随 Provider 而异，不做子串）；类型名做大小写不敏感的包含匹配
            var keyword = input.Keyword.Trim();
            var lowered = keyword.ToLowerInvariant();
            query = Guid.TryParse(keyword, out var eventId)
                ? query.Where(x => x.Event.Id == eventId || x.Event.Type.ToLower().Contains(lowered))
                : query.Where(x => x.Event.Type.ToLower().Contains(lowered));
        }

        if (input.SubscriptionId is { } subscriptionId)
        {
            query = query.Where(x => x.Delivery.SubscriptionId == subscriptionId);
        }

        if (input.Status is { } status)
        {
            query = query.Where(x => x.Delivery.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(input.EventType))
        {
            var eventType = input.EventType;
            query = query.Where(x => x.Event.Type == eventType);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Delivery.CreationTime)
            .ThenByDescending(x => x.Delivery.Id)
            .Skip(input.Offset)
            .Take(input.Limit)
            .ToListAsync(cancellationToken);

        return new PagedResult<WebhookDeliveryEntry>(totalCount, rows.Select(ToEntry));
    }

    public async Task<WebhookDeliveryEntry?> FindDeliveryAsync(Guid id, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var row = await DeliveryRows(dbContext).FirstOrDefaultAsync(x => x.Delivery.Id == id, cancellationToken);
        return row is null ? null : ToEntry(row);
    }

    public async Task<WebhookDeliveryDetailEntry?> GetDeliveryDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var row = await DeliveryRows(dbContext).FirstOrDefaultAsync(x => x.Delivery.Id == id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        // 尝试不单独实现 IMultiTenant：经已按租户过滤的投递取得
        var attempts = await dbContext.Set<WebhookAttempt>().AsNoTracking()
            .Where(x => x.DeliveryId == id)
            .OrderByDescending(x => x.AttemptedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return new WebhookDeliveryDetailEntry(
            ToEntry(row),
            row.Event.Payload,
            attempts.Select(attempt => new WebhookAttemptEntry
            {
                Id = attempt.Id,
                AttemptedAt = attempt.AttemptedAt,
                Trigger = attempt.Trigger,
                RequestHeaders = attempt.RequestHeaders,
                StatusCode = attempt.StatusCode,
                DurationMs = attempt.DurationMs,
                Response = attempt.Response,
                Error = attempt.Error
            }).ToList());
    }

    public async Task<WebhookDeliveryEntry?> ScheduleManualRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var delivery = await dbContext.Set<WebhookDelivery>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        var subscription = await dbContext.Set<WebhookSubscription>().AsNoTracking()
            .FirstAsync(x => x.Id == delivery.SubscriptionId, cancellationToken);
        var eventType = await dbContext.Set<WebhookEvent>().AsNoTracking()
            .Where(x => x.Id == delivery.EventId)
            .Select(x => x.Type)
            .FirstAsync(cancellationToken);

        deliveryQueue.ScheduleManualRetry(delivery);
        return ToEntry(delivery, eventType, subscription.Name, subscription.Url, subscription.IsEnabled);
    }

    private async Task<WebhookSubscriptionEntry?> ModifyAsync(Guid id, Action<WebhookSubscription> modify, CancellationToken cancellationToken)
    {
        var subscription = await FindTrackedAsync(id, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        modify(subscription);
        return ToEntry(subscription);
    }

    private async Task<WebhookSubscription?> FindTrackedAsync(Guid id, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        return await dbContext.Set<WebhookSubscription>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private static void Apply(WebhookSubscription subscription, WebhookSubscriptionFields fields)
    {
        subscription.Name = fields.Name;
        subscription.Description = fields.Description;
        subscription.Url = fields.Url;
        subscription.EventTypes = [.. fields.EventTypes];
        subscription.Headers = new Dictionary<string, string>(fields.Headers, StringComparer.OrdinalIgnoreCase);
    }

    private static IQueryable<DeliveryRow> DeliveryRows(TDbContext dbContext)
        => from delivery in dbContext.Set<WebhookDelivery>().AsNoTracking()
           join webhookEvent in dbContext.Set<WebhookEvent>().AsNoTracking() on delivery.EventId equals webhookEvent.Id
           join subscription in dbContext.Set<WebhookSubscription>().AsNoTracking() on delivery.SubscriptionId equals subscription.Id
           select new DeliveryRow { Delivery = delivery, Event = webhookEvent, Subscription = subscription };

    private static WebhookSubscriptionEntry ToEntry(WebhookSubscription subscription) => new()
    {
        Id = subscription.Id,
        TenantId = subscription.TenantId,
        Name = subscription.Name,
        Description = subscription.Description,
        Url = subscription.Url,
        EventTypes = subscription.EventTypes,
        Headers = subscription.Headers,
        Secret = subscription.Secret,
        PreviousSecretExpiresAt = subscription.PreviousSecretExpiresAt,
        IsEnabled = subscription.IsEnabled,
        DisabledReason = subscription.DisabledReason,
        DisabledAt = subscription.DisabledAt,
        FirstFailureAt = subscription.FirstFailureAt,
        CreationTime = subscription.CreationTime
    };

    private static WebhookDeliveryEntry ToEntry(DeliveryRow row)
        => ToEntry(row.Delivery, row.Event.Type, row.Subscription.Name, row.Subscription.Url, row.Subscription.IsEnabled);

    private static WebhookDeliveryEntry ToEntry(
        WebhookDelivery delivery,
        string eventType,
        string subscriptionName,
        string subscriptionUrl,
        bool subscriptionIsEnabled) => new()
    {
        Id = delivery.Id,
        EventId = delivery.EventId,
        EventType = eventType,
        SubscriptionId = delivery.SubscriptionId,
        SubscriptionName = subscriptionName,
        SubscriptionUrl = subscriptionUrl,
        SubscriptionIsEnabled = subscriptionIsEnabled,
        Status = delivery.Status,
        AttemptCount = delivery.AttemptCount,
        LastStatusCode = delivery.LastStatusCode,
        LastError = delivery.LastError,
        LastAttemptAt = delivery.LastAttemptAt,
        NextAttemptAt = delivery.NextAttemptAt,
        NextTrigger = delivery.NextTrigger,
        CreationTime = delivery.CreationTime
    };

    private sealed class DeliveryRow
    {
        public required WebhookDelivery Delivery { get; init; }

        public required WebhookEvent Event { get; init; }

        public required WebhookSubscription Subscription { get; init; }
    }
}
