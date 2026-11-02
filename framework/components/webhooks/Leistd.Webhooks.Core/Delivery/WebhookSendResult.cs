namespace Leistd.Webhooks.Delivery;

/// <summary>一次 webhook 请求的结果。</summary>
public sealed record WebhookSendResult
{
    /// <summary>记录的响应体最大长度（字符），超出部分截断。</summary>
    public const int MaxResponseLength = 1024;

    /// <summary>响应状态码；没有收到响应（连接失败、超时、地址被拦截）时为 <see langword="null"/>。</summary>
    public int? StatusCode { get; init; }

    /// <summary>是否成功：收到 2xx 响应。</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>本次尝试的耗时。</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>本次实际发出的请求头快照，不含 <c>webhook-signature</c>。</summary>
    public required IReadOnlyDictionary<string, string> RequestHeaders { get; init; }

    /// <summary>响应体，截断到 <see cref="MaxResponseLength"/>；没有响应时为 <see langword="null"/>。</summary>
    public string? Response { get; init; }

    /// <summary>没有收到响应时的失败原因（英文，面向运维）；收到响应时为 <see langword="null"/>。</summary>
    public string? Error { get; init; }

    /// <summary>响应的 <c>Retry-After</c> 换算成的等待时长；没有该头或无法解析时为 <see langword="null"/>。</summary>
    public TimeSpan? RetryAfter { get; init; }
}
