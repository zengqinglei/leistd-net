namespace Leistd.Webhooks.Delivery;

/// <summary>一次 webhook 请求的内容。</summary>
public sealed record WebhookSendRequest
{
    /// <summary>事件标识，即 <c>webhook-id</c>；同一事件的每次尝试（含手动重发）都相同。</summary>
    public required Guid EventId { get; init; }

    /// <summary>事件类型名。</summary>
    public required string EventType { get; init; }

    /// <summary>投递所属的订阅。</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>目标地址，绝对 http(s) URL。</summary>
    public required Uri Url { get; init; }

    /// <summary>本次签名用的时刻，即 <c>webhook-timestamp</c>。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>请求体：发布时冻结的 JSON 信封 <c>{type, timestamp, data}</c>，原样发送并参与签名。</summary>
    public required string Body { get; init; }

    /// <summary>订阅的自定义请求头。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>签名密钥明文（<c>whsec_</c> 前缀），新密钥在前；轮换过渡期内含新旧两个，各签一份。</summary>
    public required IReadOnlyList<string> Secrets { get; init; }

    /// <summary>不输出请求体与密钥。</summary>
    public override string ToString()
        => $"{nameof(WebhookSendRequest)} {{ EventId = {EventId}, EventType = {EventType}, SubscriptionId = {SubscriptionId}, Url = {Url} }}";
}
