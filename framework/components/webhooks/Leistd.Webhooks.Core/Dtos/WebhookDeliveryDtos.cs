using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.Webhooks.Delivery;

namespace Leistd.Webhooks.Dtos;

/// <summary>投递分页查询入参；按创建时间倒序。</summary>
public record GetWebhookDeliveriesInputDto : PageRequest
{
    /// <summary>在事件类型名与事件标识上做包含匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>只取该订阅的投递；为空不过滤。</summary>
    public Guid? SubscriptionId { get; init; }

    /// <summary>状态；为空不过滤，未定义的枚举值验证失败。</summary>
    /// <remarks>HTTP 端点的查询串取 camelCase 名称（<c>pending</c>、<c>succeeded</c>、<c>failed</c>），由端点转换成本属性。</remarks>
    [EnumDataType(typeof(WebhookDeliveryStatus), ErrorMessage = "{0} is not an allowed value.")]
    public WebhookDeliveryStatus? Status { get; init; }

    /// <summary>只取该事件类型的投递；为空不过滤。</summary>
    [MaxLength(CreateWebhookSubscriptionInputDto.MaxEventTypeLength, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventType { get; init; }
}

/// <summary>投递列表项：一个事件发给一个订阅的投递。</summary>
public record WebhookDeliveryOutputDto
{
    /// <summary>投递标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>事件标识，即 <c>webhook-id</c>。</summary>
    public required Guid EventId { get; init; }

    /// <summary>事件类型名。</summary>
    public required string EventType { get; init; }

    /// <summary>订阅标识。</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>订阅名。</summary>
    public required string SubscriptionName { get; init; }

    /// <summary>订阅的目标地址（当前值）。</summary>
    public required string SubscriptionUrl { get; init; }

    /// <summary>状态。</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>累计尝试次数，含手动尝试。</summary>
    public required int AttemptCount { get; init; }

    /// <summary>最近一次尝试的状态码；还没有尝试或没有收到响应时为 <see langword="null"/>。</summary>
    public int? LastStatusCode { get; init; }

    /// <summary>最近一次失败的原因，或停用时终结投递的原因；没有时为 <see langword="null"/>。</summary>
    public string? LastError { get; init; }

    /// <summary>最近一次尝试的时刻（UTC）；还没有尝试时为 <see langword="null"/>。</summary>
    public DateTime? LastAttemptAt { get; init; }

    /// <summary>下一次尝试的时刻（UTC）；只有 <see cref="WebhookDeliveryStatus.Pending"/> 时有值。</summary>
    public DateTime? NextAttemptAt { get; init; }

    /// <summary>下一次尝试的触发来源；只有 <see cref="WebhookDeliveryStatus.Pending"/> 时有值。</summary>
    public WebhookAttemptTrigger? NextTrigger { get; init; }

    /// <summary>创建时间（UTC），即事件发布时刻；统计按它分桶。</summary>
    public required DateTime CreationTime { get; init; }
}

/// <summary>投递详情：列表项各列，加载荷与全部尝试。</summary>
public sealed record WebhookDeliveryDetailOutputDto : WebhookDeliveryOutputDto
{
    /// <summary>请求体：发布时冻结的 JSON 信封 <c>{type, timestamp, data}</c>，每次尝试原样发送。</summary>
    public required string Payload { get; init; }

    /// <summary>全部尝试，按尝试时刻倒序。</summary>
    public required IReadOnlyList<WebhookAttemptOutputDto> Attempts { get; init; }
}

/// <summary>一次投递尝试。</summary>
public sealed record WebhookAttemptOutputDto
{
    /// <summary>尝试标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>尝试时刻（UTC）。</summary>
    public required DateTime AttemptedAt { get; init; }

    /// <summary>触发来源。</summary>
    public required WebhookAttemptTrigger Trigger { get; init; }

    /// <summary>本次实际发出的请求头快照，不含 <c>webhook-signature</c>；之后修改订阅的请求头不影响它。</summary>
    public required IReadOnlyDictionary<string, string> RequestHeaders { get; init; }

    /// <summary>响应状态码；没有收到响应时为 <see langword="null"/>。</summary>
    public int? StatusCode { get; init; }

    /// <summary>耗时（毫秒）。</summary>
    public required int DurationMs { get; init; }

    /// <summary>响应体，截断到 <see cref="WebhookSendResult.MaxResponseLength"/>；没有响应时为 <see langword="null"/>。</summary>
    public string? Response { get; init; }

    /// <summary>没有收到响应时的失败原因；收到响应时为 <see langword="null"/>。</summary>
    public string? Error { get; init; }
}
