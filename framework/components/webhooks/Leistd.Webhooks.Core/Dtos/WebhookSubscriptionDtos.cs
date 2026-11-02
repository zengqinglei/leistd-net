using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Management;

namespace Leistd.Webhooks.Dtos;

/// <summary>订阅分页查询入参。</summary>
public record GetWebhookSubscriptionsInputDto : PageRequest
{
    /// <summary>在名称与地址上做包含匹配（大小写不敏感）。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>只取启用（<see langword="true"/>）或停用（<see langword="false"/>）的订阅；为空不过滤。</summary>
    public bool? IsEnabled { get; init; }

    /// <summary>只取订阅了该事件类型的订阅；为空不过滤。</summary>
    [MaxLength(CreateWebhookSubscriptionInputDto.MaxEventTypeLength, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventType { get; init; }
}

/// <summary>订阅输出。</summary>
/// <remarks>不含密钥明文，只有 <see cref="SecretPrefix"/>；明文只在创建与轮换的结果里出现一次。</remarks>
public sealed record WebhookSubscriptionOutputDto
{
    /// <summary>订阅标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>名称。</summary>
    public required string Name { get; init; }

    /// <summary>目标地址。</summary>
    public required string Url { get; init; }

    /// <summary>订阅的事件类型名。</summary>
    public required IReadOnlyList<string> EventTypes { get; init; }

    /// <summary>自定义请求头。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>当前密钥的前缀（含 <c>whsec_</c>），供核对接收方配置。</summary>
    public required string SecretPrefix { get; init; }

    /// <summary>上一把密钥继续签名的截止时刻（UTC）；不在轮换过渡期时为 <see langword="null"/>。</summary>
    public DateTime? PreviousSecretExpiresAt { get; init; }

    /// <summary>是否启用。</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>停用原因；启用时为 <see langword="null"/>。</summary>
    public WebhookDisabledReason? DisabledReason { get; init; }

    /// <summary>停用时刻（UTC）；启用时为 <see langword="null"/>。</summary>
    public DateTime? DisabledAt { get; init; }

    /// <summary>本轮连续失败的起点（UTC）；任何一次成功或重新启用后为 <see langword="null"/>。</summary>
    public DateTime? FirstFailureAt { get; init; }

    /// <summary>最近一次投递；还没有投递时为 <see langword="null"/>。</summary>
    public WebhookSubscriptionLastDeliveryDto? LastDelivery { get; init; }

    /// <summary>近 7 天成功率（0–1）：最近 7×24 小时内创建的投递中，成功数 ÷（成功 + 失败）；没有终态投递时为 <see langword="null"/>。</summary>
    public double? SuccessRateLast7Days { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    public required DateTime CreationTime { get; init; }
}

/// <summary>订阅的最近一次投递摘要。</summary>
public sealed record WebhookSubscriptionLastDeliveryDto
{
    /// <summary>投递标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>状态。</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>最近一次尝试的状态码；还没有尝试或没有收到响应时为 <see langword="null"/>。</summary>
    public int? LastStatusCode { get; init; }

    /// <summary>投递创建时间（UTC），即事件发布时刻。</summary>
    public required DateTime CreationTime { get; init; }
}

/// <summary>创建订阅入参。</summary>
public record CreateWebhookSubscriptionInputDto
{
    /// <summary>名称的最大长度。</summary>
    public const int MaxNameLength = 128;

    /// <summary>地址的最大长度。</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>事件类型名的最大长度。</summary>
    public const int MaxEventTypeLength = 128;

    /// <summary>单个订阅最多订阅的事件类型数。</summary>
    public const int MaxEventTypeCount = 100;

    /// <summary>单个订阅最多的自定义请求头数。</summary>
    public const int MaxHeaderCount = 20;

    /// <summary>名称。</summary>
    [Required]
    [MaxLength(MaxNameLength)]
    public string Name { get; init; } = null!;

    /// <summary>目标地址，须是绝对 http(s) URL；内网地址在连接时按 <c>Leistd:Webhooks:AllowedNetworks</c> 拦截，此处不解析。</summary>
    [Required]
    [MaxLength(MaxUrlLength)]
    public string Url { get; init; } = null!;

    /// <summary>订阅的事件类型名，至少一个，须已登记。</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(MaxEventTypeCount)]
    public IReadOnlyList<string> EventTypes { get; init; } = null!;

    /// <summary>自定义请求头；为空表示没有。不能覆盖 <c>webhook-*</c> 与 <c>Content-Type</c> 等由组件写入的头。</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>更新订阅入参；字段全部按传入值覆盖，长度上限同 <see cref="CreateWebhookSubscriptionInputDto"/>。</summary>
/// <remarks>启停与密钥轮换各有自己的用例，不经本入参。</remarks>
public record UpdateWebhookSubscriptionInputDto
{
    /// <summary>名称。</summary>
    [Required]
    [MaxLength(CreateWebhookSubscriptionInputDto.MaxNameLength)]
    public string Name { get; init; } = null!;

    /// <summary>目标地址，须是绝对 http(s) URL。</summary>
    [Required]
    [MaxLength(CreateWebhookSubscriptionInputDto.MaxUrlLength)]
    public string Url { get; init; } = null!;

    /// <summary>订阅的事件类型名，至少一个，须已登记。</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(CreateWebhookSubscriptionInputDto.MaxEventTypeCount)]
    public IReadOnlyList<string> EventTypes { get; init; } = null!;

    /// <summary>自定义请求头；为空即清空。只影响之后的尝试，已有尝试保留当时的头部快照。</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>创建订阅或轮换密钥的结果：订阅与一次性明文密钥。</summary>
/// <remarks>明文密钥只在这里出现一次，之后只能看到前缀。</remarks>
public sealed record WebhookSubscriptionSecretOutputDto
{
    /// <summary>订阅。</summary>
    public required WebhookSubscriptionOutputDto Subscription { get; init; }

    /// <summary>当前密钥明文（<c>whsec_</c> 前缀），接收方据此验签。</summary>
    public required string Secret { get; init; }

    /// <summary>不输出密钥。</summary>
    public override string ToString()
        => $"{nameof(WebhookSubscriptionSecretOutputDto)} {{ SubscriptionId = {Subscription.Id} }}";
}
