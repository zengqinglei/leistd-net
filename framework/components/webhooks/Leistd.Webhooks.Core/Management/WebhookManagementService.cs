using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.UnitOfWork;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Leistd.Webhooks.Stores;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.Management;

// 规格"管理端点"的用例：输入校验、启停与重发判定、密钥生成与轮换、变更事件与输出组装；读写经存储契约。
// 写操作在工作单元里完成，宿主已有工作单元时并入。订阅变更事件在写入保存后、工作单元完成前发布，
// 由事件总线推迟到提交后分发；只在状态真正改变时发布。
internal sealed class WebhookManagementService(
    IWebhookManagementStore store,
    IUnitOfWorkManager unitOfWorkManager,
    IWebhookDefinitionManager definitionManager,
    WebhookSecretProtector secretProtector,
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider timeProvider,
    ILocalEventBus? localEventBus = null) : IWebhookManagementService
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

        var page = await store.GetSubscriptionsAsync(input, cancellationToken);
        return new PagedResult<WebhookSubscriptionOutputDto>(page.TotalCount, await ToOutputsAsync(page.Items, cancellationToken));
    }

    public async Task<WebhookSubscriptionOutputDto> GetSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subscription = await store.FindSubscriptionAsync(id, cancellationToken) ?? throw SubscriptionNotFound();
        return await ToOutputAsync(subscription, cancellationToken);
    }

    public async Task<WebhookSubscriptionSecretOutputDto> CreateSubscriptionAsync(
        CreateWebhookSubscriptionInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        var fields = NormalizeFields(input.Name, input.Description, input.Url, input.EventTypes, input.Headers);

        using var unitOfWork = unitOfWorkManager.Begin();
        var (secret, protectedSecret) = secretProtector.CreateSecret();
        var subscription = await store.AddSubscriptionAsync(
            fields, protectedSecret, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await PublishAsync(subscription, WebhookSubscriptionChangeKind.Created, cancellationToken: cancellationToken);

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
        var fields = NormalizeFields(input.Name, input.Description, input.Url, input.EventTypes, input.Headers);

        // 修改成功即发布，不比较前后值
        return await WriteSubscriptionAsync(
            async () => (await store.UpdateSubscriptionAsync(id, fields, cancellationToken), WebhookSubscriptionChangeKind.Updated),
            cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var subscription = await store.DeleteSubscriptionAsync(id, cancellationToken) ?? throw SubscriptionNotFound();
        await PublishAsync(subscription, WebhookSubscriptionChangeKind.Deleted, cancellationToken: cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
    }

    public Task<WebhookSubscriptionOutputDto> EnableSubscriptionAsync(
        Guid id,
        bool retryFailed = false,
        CancellationToken cancellationToken = default)
        => WriteSubscriptionAsync(async () =>
        {
            if (await store.EnableSubscriptionAsync(id, cancellationToken) is not { } result)
            {
                return (null, null);
            }

            // 现存的全部失败投递，不论何时创建、是否发出过请求：停用时被终结的与停用前就已失败的同样重发
            if (retryFailed)
            {
                await store.RetryFailedDeliveriesAsync(id, cancellationToken);
            }

            return (result.Subscription, result.Changed ? WebhookSubscriptionChangeKind.Enabled : null);
        }, cancellationToken);

    public Task<WebhookSubscriptionOutputDto> DisableSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        => WriteSubscriptionAsync(async () =>
            await store.DisableSubscriptionAsync(id, WebhookDisabledReason.Manual, cancellationToken) is { } result
                ? (result.Subscription, result.Changed ? WebhookSubscriptionChangeKind.Disabled : null)
                : (null, null),
            cancellationToken,
            WebhookDisabledReason.Manual);

    public async Task<WebhookSubscriptionSecretOutputDto> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (secret, protectedSecret) = secretProtector.CreateSecret();

        // 过渡期为 0 时不保留旧密钥，轮换立即生效
        var overlap = options.CurrentValue.SecretRotationOverlap;
        DateTime? previousSecretExpiresAt = overlap > TimeSpan.Zero ? timeProvider.GetUtcNow().UtcDateTime + overlap : null;

        var output = await WriteSubscriptionAsync(
            async () => (await store.RotateSecretAsync(id, protectedSecret, previousSecretExpiresAt, cancellationToken),
                WebhookSubscriptionChangeKind.SecretRotated),
            cancellationToken);
        return new WebhookSubscriptionSecretOutputDto { Subscription = output, Secret = secret };
    }

    public async Task<WebhookDeliveryOutputDto> SendTestEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var subscription = await store.FindSubscriptionAsync(id, cancellationToken) ?? throw SubscriptionNotFound();
        if (!subscription.IsEnabled)
        {
            throw SubscriptionDisabled();
        }

        // 只投给这一个订阅：订阅不必订阅测试类型
        var delivery = await store.AddDeliveryAsync(
            subscription, WebhookDefinition.TestEventType, new { subscriptionId = subscription.Id }, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return ToOutput(delivery);
    }

    public async Task<PagedResult<WebhookDeliveryOutputDto>> GetDeliveriesAsync(
        GetWebhookDeliveriesInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var page = await store.GetDeliveriesAsync(input, cancellationToken);
        return new PagedResult<WebhookDeliveryOutputDto>(page.TotalCount, page.Items.Select(ToOutput));
    }

    public async Task<WebhookDeliveryDetailOutputDto> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await store.GetDeliveryDetailAsync(id, cancellationToken) ?? throw DeliveryNotFound();

        var output = ToOutput(detail.Delivery);
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
            Payload = detail.Payload,
            Attempts = detail.Attempts.Select(attempt => new WebhookAttemptOutputDto
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
        var delivery = await store.FindDeliveryAsync(id, cancellationToken) ?? throw DeliveryNotFound();

        // 规格"投递链路"第 6 步：只对终态开放，重试中的投递已有排期
        if (delivery.Status == WebhookDeliveryStatus.Pending)
        {
            throw new BusinessException(
                WebhookErrorCodes.DeliveryNotRetryable, "The delivery is still being retried and cannot be resent manually.");
        }

        if (!delivery.SubscriptionIsEnabled)
        {
            throw SubscriptionDisabled();
        }

        var scheduled = await store.ScheduleManualRetryAsync(id, cancellationToken) ?? throw DeliveryNotFound();
        await unitOfWork.CompleteAsync(cancellationToken);
        return ToOutput(scheduled);
    }

    // 在工作单元里执行 write 并保存，返回改后的输出（含最近投递与成功率）。
    // write 返回改后的订阅（找不到为 null）与要发布的变更类别（状态没变时为 null）。
    private async Task<WebhookSubscriptionOutputDto> WriteSubscriptionAsync(
        Func<Task<(WebhookSubscriptionEntry? Subscription, WebhookSubscriptionChangeKind? Change)>> write,
        CancellationToken cancellationToken,
        WebhookDisabledReason? disabledReason = null)
    {
        using var unitOfWork = unitOfWorkManager.Begin();
        var (subscription, change) = await write();
        if (subscription is null)
        {
            throw SubscriptionNotFound();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (change is { } kind)
        {
            await PublishAsync(subscription, kind, disabledReason, cancellationToken);
        }

        var output = await ToOutputAsync(subscription, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
        return output;
    }

    private async Task PublishAsync(
        WebhookSubscriptionEntry subscription,
        WebhookSubscriptionChangeKind change,
        WebhookDisabledReason? disabledReason = null,
        CancellationToken cancellationToken = default)
    {
        if (localEventBus is not null)
        {
            await localEventBus.PublishAsync(
                new WebhookSubscriptionChangedEvent(subscription.Id, subscription.Name, change, disabledReason), cancellationToken);
        }
    }

    private async Task<WebhookSubscriptionOutputDto> ToOutputAsync(WebhookSubscriptionEntry subscription, CancellationToken cancellationToken)
        => (await ToOutputsAsync([subscription], cancellationToken))[0];

    // 最近一次投递与近 7 天成功率：对这一页的订阅一次取回，不逐行查。
    private async Task<IReadOnlyList<WebhookSubscriptionOutputDto>> ToOutputsAsync(
        IReadOnlyList<WebhookSubscriptionEntry> subscriptions,
        CancellationToken cancellationToken)
    {
        if (subscriptions.Count == 0)
        {
            return [];
        }

        var since = timeProvider.GetUtcNow().UtcDateTime - SuccessRateWindow;
        var summaries = await store.GetDeliverySummariesAsync(
            subscriptions.Select(x => x.Id).ToList(), since, cancellationToken);

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
    private string DescribeSecret(WebhookSubscriptionEntry subscription)
    {
        var secret = secretProtector.Unprotect(subscription.Id, subscription.Secret);
        return secret[..Math.Min(secret.Length, WebhookSigner.SecretPrefix.Length + SecretPrefixLength)];
    }

    private static WebhookDeliveryOutputDto ToOutput(WebhookDeliveryEntry delivery) => new()
    {
        Id = delivery.Id,
        EventId = delivery.EventId,
        EventType = delivery.EventType,
        SubscriptionId = delivery.SubscriptionId,
        SubscriptionName = delivery.SubscriptionName,
        SubscriptionUrl = delivery.SubscriptionUrl,
        Status = delivery.Status,
        AttemptCount = delivery.AttemptCount,
        LastStatusCode = delivery.LastStatusCode,
        LastError = delivery.LastError,
        LastAttemptAt = delivery.LastAttemptAt,
        NextAttemptAt = delivery.Status == WebhookDeliveryStatus.Pending ? delivery.NextAttemptAt : null,
        NextTrigger = delivery.Status == WebhookDeliveryStatus.Pending ? delivery.NextTrigger : null,
        CreationTime = delivery.CreationTime
    };

    private WebhookSubscriptionFields NormalizeFields(
        string name,
        string? description,
        string url,
        IReadOnlyList<string> eventTypes,
        IReadOnlyDictionary<string, string>? headers)
    {
        // 校验顺序：事件类型、地址、请求头
        var normalizedEventTypes = NormalizeEventTypes(eventTypes);
        var normalizedUrl = NormalizeUrl(url);
        var normalizedHeaders = NormalizeHeaders(headers);
        return new WebhookSubscriptionFields(
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            normalizedUrl,
            normalizedEventTypes,
            normalizedHeaders);
    }

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

    private static ValidationException Invalid(string member, string message)
        => new(new ValidationResult(message, [member]), validatingAttribute: null, value: null);

    private static BusinessException SubscriptionNotFound()
        => new(WebhookErrorCodes.SubscriptionNotFound, "The webhook subscription does not exist.");

    private static BusinessException DeliveryNotFound()
        => new(WebhookErrorCodes.DeliveryNotFound, "The webhook delivery does not exist.");

    private static BusinessException SubscriptionDisabled()
        => new(WebhookErrorCodes.SubscriptionDisabled, "The webhook subscription is disabled.");
}
