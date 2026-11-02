using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Publishing;
using Leistd.Webhooks.EntityFrameworkCore.Subscriptions;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore.Management;

// 规格"管理端点"的用例。租户隔离由 IMultiTenant 的全局查询过滤器承担：找不到即 404，不区分"别的租户的"。
// 写操作在工作单元里完成，宿主已有工作单元时并入；停用、排期与密钥沿用投递链路的内部服务，不另写一份。
// 订阅变更事件在写入保存后、工作单元完成前发布，由事件总线推迟到提交后分发；停用的事件由停用服务发布。
internal sealed class EfCoreWebhookManagementService<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    ICurrentTenant currentTenant,
    IWebhookDefinitionManager definitionManager,
    WebhookSecretProtector secretProtector,
    WebhookSubscriptionDeactivator<TDbContext> deactivator,
    WebhookDeliveryQueue<TDbContext> deliveryQueue,
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider timeProvider,
    ILocalEventBus? localEventBus = null) : IWebhookManagementService
    where TDbContext : DbContext
{
    // 近 7 天成功率的窗口：最近 7×24 小时，不按日历对齐
    internal static readonly TimeSpan SuccessRateWindow = TimeSpan.FromDays(7);

    // 列表与详情只显示的密钥前缀：whsec_ 之后再露 4 个字符，足够核对接收方配置
    internal const int SecretPrefixLength = 4;

    public Task<IReadOnlyList<WebhookEventTypeOutputDto>> GetEventTypesAsync(CancellationToken cancellationToken = default)
    {
        // 测试事件只经测试端点发给指定订阅，不作为可订阅的类型列出
        IReadOnlyList<WebhookEventTypeOutputDto> types = definitionManager.GetAll()
            .Where(definition => definition.Name != WebhookDefinition.TestEventType)
            .Select(definition => new WebhookEventTypeOutputDto
            {
                Name = definition.Name,
                Group = definition.Group,
                Description = definition.Description
            })
            .ToList();
        return Task.FromResult(types);
    }

    public async Task<PagedResult<WebhookSubscriptionOutputDto>> GetSubscriptionsAsync(
        GetWebhookSubscriptionsInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

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

        return new PagedResult<WebhookSubscriptionOutputDto>(totalCount, await ToOutputsAsync(subscriptions, cancellationToken));
    }

    public async Task<WebhookSubscriptionOutputDto> GetSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = await dbContext.Set<WebhookSubscription>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw SubscriptionNotFound();
        return await ToOutputAsync(subscription, cancellationToken);
    }

    public async Task<WebhookSubscriptionSecretOutputDto> CreateSubscriptionAsync(
        CreateWebhookSubscriptionInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var eventTypes = NormalizeEventTypes(input.EventTypes);
        var url = NormalizeUrl(input.Url);
        var headers = NormalizeHeaders(input.Headers);

        using var unitOfWork = unitOfWorkManager.Begin();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var (secret, protectedSecret) = secretProtector.CreateSecret();
        var subscription = new WebhookSubscription
        {
            // 与过滤口径一致：写入时显式取当前租户，不依赖宿主上下文代填
            TenantId = currentTenant.Id,
            Name = input.Name.Trim(),
            Description = NormalizeDescription(input.Description),
            Url = url,
            EventTypes = eventTypes,
            Headers = headers,
            Secret = protectedSecret,
            CreationTime = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Set<WebhookSubscription>().Add(subscription);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await PublishAsync(subscription, WebhookSubscriptionChangeKind.Created, cancellationToken);

        var output = await ToOutputAsync(subscription, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return new WebhookSubscriptionSecretOutputDto { Subscription = output, Secret = secret };
    }

    public async Task<WebhookSubscriptionOutputDto> UpdateSubscriptionAsync(
        Guid id,
        UpdateWebhookSubscriptionInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var eventTypes = NormalizeEventTypes(input.EventTypes);
        var url = NormalizeUrl(input.Url);
        var headers = NormalizeHeaders(input.Headers);

        return await WriteSubscriptionAsync(id, subscription =>
        {
            subscription.Name = input.Name.Trim();
            subscription.Description = NormalizeDescription(input.Description);
            subscription.Url = url;
            subscription.EventTypes = eventTypes;
            subscription.Headers = headers;
            return Task.FromResult<WebhookSubscriptionChangeKind?>(WebhookSubscriptionChangeKind.Updated);
        }, cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = await FindSubscriptionAsync(dbContext, id, cancellationToken);

        // 加载后 RemoveRange，不用 ExecuteDelete：InMemory 不支持，且要经过上下文上的拦截器；不依赖库里的级联
        var deliveries = await dbContext.Set<WebhookDelivery>().Where(x => x.SubscriptionId == id).ToListAsync(cancellationToken);
        var deliveryIds = deliveries.Select(x => x.Id).ToList();
        var attempts = await dbContext.Set<WebhookAttempt>().Where(x => deliveryIds.Contains(x.DeliveryId)).ToListAsync(cancellationToken);

        dbContext.Set<WebhookAttempt>().RemoveRange(attempts);
        dbContext.Set<WebhookDelivery>().RemoveRange(deliveries);
        dbContext.Set<WebhookSubscription>().Remove(subscription);
        await PublishAsync(subscription, WebhookSubscriptionChangeKind.Deleted, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
    }

    public Task<WebhookSubscriptionOutputDto> EnableSubscriptionAsync(
        Guid id,
        bool retryFailed = false,
        CancellationToken cancellationToken = default)
        => WriteSubscriptionAsync(id, async subscription =>
        {
            WebhookSubscriptionChangeKind? change = null;
            if (!subscription.IsEnabled)
            {
                subscription.IsEnabled = true;
                subscription.DisabledReason = null;
                subscription.DisabledAt = null;
                subscription.FirstFailureAt = null;
                change = WebhookSubscriptionChangeKind.Enabled;
            }

            if (!retryFailed)
            {
                return change;
            }

            // 现存的全部失败投递，不论何时创建、是否发出过请求：停用时被终结的与停用前就已失败的同样重发
            var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
            var failed = await dbContext.Set<WebhookDelivery>()
                .Where(x => x.SubscriptionId == subscription.Id && x.Status == WebhookDeliveryStatus.Failed)
                .ToListAsync(cancellationToken);
            foreach (var delivery in failed)
            {
                deliveryQueue.ScheduleManualRetry(delivery);
            }

            return change;
        }, cancellationToken);

    public Task<WebhookSubscriptionOutputDto> DisableSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        => WriteSubscriptionAsync(
            id,
            async subscription =>
            {
                await deactivator.DisableAsync(subscription, WebhookDisabledReason.Manual, cancellationToken);
                return null;
            },
            cancellationToken);

    public async Task<WebhookSubscriptionSecretOutputDto> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string secret = null!;
        var output = await WriteSubscriptionAsync(id, subscription =>
        {
            (secret, var protectedSecret) = secretProtector.CreateSecret();

            // 过渡期为 0 时不保留旧密钥，轮换立即生效
            var overlap = options.CurrentValue.SecretRotationOverlap;
            if (overlap > TimeSpan.Zero)
            {
                subscription.PreviousSecret = subscription.Secret;
                subscription.PreviousSecretExpiresAt = timeProvider.GetUtcNow().UtcDateTime + overlap;
            }
            else
            {
                subscription.PreviousSecret = null;
                subscription.PreviousSecretExpiresAt = null;
            }

            subscription.Secret = protectedSecret;
            return Task.FromResult<WebhookSubscriptionChangeKind?>(WebhookSubscriptionChangeKind.SecretRotated);
        }, cancellationToken);
        return new WebhookSubscriptionSecretOutputDto { Subscription = output, Secret = secret };
    }

    public async Task<WebhookDeliveryOutputDto> SendTestEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = await FindSubscriptionAsync(dbContext, id, cancellationToken);
        if (!subscription.IsEnabled)
        {
            throw SubscriptionDisabled();
        }

        // 只投给这一个订阅，不经发布器的类型匹配：订阅不必订阅测试类型
        var now = timeProvider.GetUtcNow();
        var webhookEvent = EfCoreWebhookPublisher<TDbContext>.CreateEvent(
            WebhookDefinition.TestEventType, new { subscriptionId = subscription.Id }, subscription.TenantId, now);
        var delivery = EfCoreWebhookPublisher<TDbContext>.CreateDelivery(webhookEvent, subscription.Id, now.UtcDateTime);
        dbContext.Set<WebhookEvent>().Add(webhookEvent);
        dbContext.Set<WebhookDelivery>().Add(delivery);
        deliveryQueue.EnqueueAfterCommit([delivery]);
        await unitOfWork.CompleteAsync(cancellationToken);

        return ToOutput(delivery, webhookEvent.Type, subscription);
    }

    public async Task<PagedResult<WebhookDeliveryOutputDto>> GetDeliveriesAsync(
        GetWebhookDeliveriesInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

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

        return new PagedResult<WebhookDeliveryOutputDto>(
            totalCount,
            rows.Select(row => ToOutput(row.Delivery, row.Event.Type, row.Subscription)));
    }

    public async Task<WebhookDeliveryDetailOutputDto> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var row = await DeliveryRows(dbContext).FirstOrDefaultAsync(x => x.Delivery.Id == id, cancellationToken)
                  ?? throw DeliveryNotFound();

        // 尝试不单独实现 IMultiTenant：经已按租户过滤的投递取得
        var attempts = await dbContext.Set<WebhookAttempt>().AsNoTracking()
            .Where(x => x.DeliveryId == id)
            .OrderByDescending(x => x.AttemptedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var output = ToOutput(row.Delivery, row.Event.Type, row.Subscription);
        return new WebhookDeliveryDetailOutputDto
        {
            Id = output.Id,
            EventId = output.EventId,
            EventType = output.EventType,
            SubscriptionId = output.SubscriptionId,
            SubscriptionName = output.SubscriptionName,
            SubscriptionUrl = output.SubscriptionUrl,
            Status = output.Status,
            AttemptCount = output.AttemptCount,
            LastStatusCode = output.LastStatusCode,
            LastError = output.LastError,
            LastAttemptAt = output.LastAttemptAt,
            NextAttemptAt = output.NextAttemptAt,
            NextTrigger = output.NextTrigger,
            CreationTime = output.CreationTime,
            Payload = row.Event.Payload,
            Attempts = attempts.Select(attempt => new WebhookAttemptOutputDto
            {
                Id = attempt.Id,
                AttemptedAt = attempt.AttemptedAt,
                Trigger = attempt.Trigger,
                RequestHeaders = attempt.RequestHeaders,
                StatusCode = attempt.StatusCode,
                DurationMs = (int)Math.Min(attempt.DurationMs, int.MaxValue),
                Response = attempt.Response,
                Error = attempt.Error
            }).ToList()
        };
    }

    public async Task<WebhookDeliveryOutputDto> RetryDeliveryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var delivery = await dbContext.Set<WebhookDelivery>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                       ?? throw DeliveryNotFound();

        // 规格"投递链路"第 6 步：只对终态开放，重试中的投递已有排期
        if (delivery.Status == WebhookDeliveryStatus.Pending)
        {
            throw new BusinessException(
                WebhookErrorCodes.DeliveryNotRetryable, "The delivery is still being retried and cannot be resent manually.");
        }

        var subscription = await dbContext.Set<WebhookSubscription>().AsNoTracking()
            .FirstAsync(x => x.Id == delivery.SubscriptionId, cancellationToken);
        if (!subscription.IsEnabled)
        {
            throw SubscriptionDisabled();
        }

        var eventType = await dbContext.Set<WebhookEvent>().AsNoTracking()
            .Where(x => x.Id == delivery.EventId)
            .Select(x => x.Type)
            .FirstAsync(cancellationToken);

        deliveryQueue.ScheduleManualRetry(delivery);
        await unitOfWork.CompleteAsync(cancellationToken);
        return ToOutput(delivery, eventType, subscription);
    }

    // 在工作单元里加载订阅、交给 change 修改并保存，返回改后的输出（含最近投递与成功率）。
    // change 返回要发布的变更类别；不需要发布（或已由停用服务发布）时返回 null。
    private async Task<WebhookSubscriptionOutputDto> WriteSubscriptionAsync(
        Guid id,
        Func<WebhookSubscription, Task<WebhookSubscriptionChangeKind?>> change,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var subscription = await FindSubscriptionAsync(dbContext, id, cancellationToken);
        var changed = await change(subscription);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (changed is { } kind)
        {
            await PublishAsync(subscription, kind, cancellationToken);
        }

        var output = await ToOutputAsync(subscription, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return output;
    }

    private async Task PublishAsync(WebhookSubscription subscription, WebhookSubscriptionChangeKind change, CancellationToken cancellationToken)
    {
        if (localEventBus is not null)
        {
            await localEventBus.PublishAsync(
                new WebhookSubscriptionChangedEvent(subscription.Id, subscription.Name, change), cancellationToken);
        }
    }

    private static async Task<WebhookSubscription> FindSubscriptionAsync(
        TDbContext dbContext,
        Guid id,
        CancellationToken cancellationToken)
        => await dbContext.Set<WebhookSubscription>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
           ?? throw SubscriptionNotFound();

    private static IQueryable<DeliveryRow> DeliveryRows(TDbContext dbContext)
        => from delivery in dbContext.Set<WebhookDelivery>().AsNoTracking()
           join webhookEvent in dbContext.Set<WebhookEvent>().AsNoTracking() on delivery.EventId equals webhookEvent.Id
           join subscription in dbContext.Set<WebhookSubscription>().AsNoTracking() on delivery.SubscriptionId equals subscription.Id
           select new DeliveryRow { Delivery = delivery, Event = webhookEvent, Subscription = subscription };

    private async Task<WebhookSubscriptionOutputDto> ToOutputAsync(WebhookSubscription subscription, CancellationToken cancellationToken)
        => (await ToOutputsAsync([subscription], cancellationToken))[0];

    // 最近一次投递与近 7 天成功率：对这一页的订阅一次分组查询，不逐行查。
    private async Task<IReadOnlyList<WebhookSubscriptionOutputDto>> ToOutputsAsync(
        IReadOnlyList<WebhookSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        if (subscriptions.Count == 0)
        {
            return [];
        }

        var ids = subscriptions.Select(x => x.Id).ToList();
        var since = timeProvider.GetUtcNow().UtcDateTime - SuccessRateWindow;
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
            .ToDictionaryAsync(x => x.SubscriptionId, cancellationToken);

        return subscriptions.Select(subscription =>
        {
            summaries.TryGetValue(subscription.Id, out var summary);
            var terminal = (summary?.Succeeded ?? 0) + (summary?.Failed ?? 0);
            return new WebhookSubscriptionOutputDto
            {
                Id = subscription.Id,
                Name = subscription.Name,
                Description = subscription.Description,
                Url = subscription.Url,
                EventTypes = subscription.EventTypes,
                Headers = subscription.Headers,
                SecretPrefix = DescribeSecret(subscription),
                PreviousSecretExpiresAt = subscription.PreviousSecretExpiresAt,
                IsEnabled = subscription.IsEnabled,
                DisabledReason = subscription.DisabledReason,
                DisabledAt = subscription.DisabledAt,
                FirstFailureAt = subscription.FirstFailureAt,
                LastDelivery = summary?.Last,
                SuccessRateLast7Days = terminal == 0 ? null : (double)summary!.Succeeded / terminal,
                CreationTime = subscription.CreationTime
            };
        }).ToList();
    }

    // 没有存前缀列：解密后截取，明文不离开本方法
    private string DescribeSecret(WebhookSubscription subscription)
    {
        var secret = secretProtector.Unprotect(subscription, subscription.Secret);
        return secret[..Math.Min(secret.Length, WebhookSigner.SecretPrefix.Length + SecretPrefixLength)];
    }

    private static WebhookDeliveryOutputDto ToOutput(WebhookDelivery delivery, string eventType, WebhookSubscription subscription) => new()
    {
        Id = delivery.Id,
        EventId = delivery.EventId,
        EventType = eventType,
        SubscriptionId = subscription.Id,
        SubscriptionName = subscription.Name,
        SubscriptionUrl = subscription.Url,
        Status = delivery.Status,
        AttemptCount = delivery.AttemptCount,
        LastStatusCode = delivery.LastStatusCode,
        LastError = delivery.LastError,
        LastAttemptAt = delivery.LastAttemptAt,
        NextAttemptAt = delivery.Status == WebhookDeliveryStatus.Pending ? delivery.NextAttemptAt : null,
        NextTrigger = delivery.Status == WebhookDeliveryStatus.Pending ? delivery.NextTrigger : null,
        CreationTime = delivery.CreationTime
    };

    // 只能是业务登记的类型（测试事件不可订阅），或单独的 "*"（订阅全部，含以后登记的类型）；去重保序。
    private List<string> NormalizeEventTypes(IReadOnlyList<string> eventTypes)
    {
        var normalized = new List<string>(eventTypes.Count);
        foreach (var eventType in eventTypes)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw Invalid(nameof(CreateWebhookSubscriptionInputDto.EventTypes), "EventTypes cannot contain empty values.");
            }

            if (eventType != WebhookDefinition.AllEventTypes
                && (eventType == WebhookDefinition.TestEventType || definitionManager.GetOrNull(eventType) is null))
            {
                throw Invalid(nameof(CreateWebhookSubscriptionInputDto.EventTypes), $"The event type '{eventType}' is not registered.");
            }

            if (!normalized.Contains(eventType, StringComparer.Ordinal))
            {
                normalized.Add(eventType);
            }
        }

        if (normalized.Count > 1 && normalized.Contains(WebhookDefinition.AllEventTypes, StringComparer.Ordinal))
        {
            throw Invalid(
                nameof(CreateWebhookSubscriptionInputDto.EventTypes),
                $"'{WebhookDefinition.AllEventTypes}' subscribes to all event types and cannot be combined with other event types.");
        }

        return normalized;
    }

    // 只校验是绝对 http(s) URL；内网地址在连接时由发送器拦截（规格"投递链路"第 4 步）。
    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw Invalid(nameof(CreateWebhookSubscriptionInputDto.Url), "The URL must be an absolute http or https URL.");
        }

        return trimmed;
    }

    // 自定义头不能覆盖组件写入的头（webhook-*、Content-Type）与传输层管理的头；名称须是 HTTP 令牌，值不含控制字符。
    private static Dictionary<string, string> NormalizeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        const string field = nameof(CreateWebhookSubscriptionInputDto.Headers);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is null)
        {
            return normalized;
        }

        if (headers.Count > CreateWebhookSubscriptionInputDto.MaxHeaderCount)
        {
            throw Invalid(field, $"No more than {CreateWebhookSubscriptionInputDto.MaxHeaderCount} custom headers are allowed.");
        }

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrEmpty(name) || !name.All(IsTokenChar))
            {
                throw Invalid(field, $"'{name}' is not a valid header name.");
            }

            if (IsReservedHeader(name))
            {
                throw Invalid(field, "Custom headers cannot override webhook-* or Content-Type headers.");
            }

            if (value is null || value.Any(char.IsControl))
            {
                throw Invalid(field, $"The value of header '{name}' cannot be empty or contain control characters.");
            }

            if (!normalized.TryAdd(name, value))
            {
                throw Invalid(field, $"The header '{name}' is specified more than once.");
            }
        }

        return normalized;
    }

    private static bool IsReservedHeader(string name)
        => name.StartsWith("webhook-", StringComparison.OrdinalIgnoreCase)
           || ReservedHeaders.Contains(name);

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Host", "User-Agent"
    };

    // RFC 9110 §5.6.2 token
    private static bool IsTokenChar(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
           || "!#$%&'*+-.^_`|~".Contains(c);

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ValidationException Invalid(string member, string message)
        => new(new ValidationResult(message, [member]), validatingAttribute: null, value: null);

    private static BusinessException SubscriptionNotFound()
        => new(WebhookErrorCodes.SubscriptionNotFound, "The webhook subscription does not exist.");

    private static BusinessException DeliveryNotFound()
        => new(WebhookErrorCodes.DeliveryNotFound, "The webhook delivery does not exist.");

    private static BusinessException SubscriptionDisabled()
        => new(WebhookErrorCodes.SubscriptionDisabled, "The webhook subscription is disabled.");

    private sealed class DeliveryRow
    {
        public required WebhookDelivery Delivery { get; init; }

        public required WebhookEvent Event { get; init; }

        public required WebhookSubscription Subscription { get; init; }
    }
}
