using Leistd.MultiTenancy.Tenancy;

namespace Leistd.Webhooks.EntityFrameworkCore.Entities;

/// <summary>一次已发布的 webhook 事件（发件箱记录），载荷在发布时冻结。</summary>
/// <remarks>实现 <see cref="IMultiTenant"/>：归属发布时的租户上下文。</remarks>
public class WebhookEvent : IMultiTenant
{
    /// <summary>事件 ID（有序 Guid v7），即 <c>webhook-id</c>；同一事件的每次尝试都相同。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>事件类型名。</summary>
    public string Type { get; set; } = default!;

    /// <summary>请求体：JSON 信封 <c>{type, timestamp, data}</c>，原样发送并参与签名。</summary>
    public string Payload { get; set; } = default!;

    /// <summary>发布时刻（UTC），即信封里的 <c>timestamp</c>。</summary>
    public DateTime OccurredAt { get; set; }
}
