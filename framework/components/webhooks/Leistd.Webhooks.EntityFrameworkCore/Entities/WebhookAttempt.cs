using Leistd.Webhooks.Delivery;

namespace Leistd.Webhooks.EntityFrameworkCore.Entities;

/// <summary>一次投递尝试的记录。</summary>
/// <remarks>不单独实现 <c>IMultiTenant</c>：只经所属投递读取，租户隔离随投递。</remarks>
public class WebhookAttempt
{
    /// <summary>尝试 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>投递 ID。</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>尝试开始时刻（UTC）。</summary>
    public DateTime AttemptedAt { get; set; }

    /// <summary>触发来源。</summary>
    public WebhookAttemptTrigger Trigger { get; set; }

    /// <summary>本次实际发出的请求头快照，不含 <c>webhook-signature</c>；之后改订阅的请求头不影响它。</summary>
    public Dictionary<string, string> RequestHeaders { get; set; } = [];

    /// <summary>响应状态码；没有收到响应时为 <see langword="null"/>。</summary>
    public int? StatusCode { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long DurationMs { get; set; }

    /// <summary>响应体，截断到 <see cref="WebhookSendResult.MaxResponseLength"/>。</summary>
    public string? Response { get; set; }

    /// <summary>没有收到响应时的失败原因。</summary>
    public string? Error { get; set; }
}
