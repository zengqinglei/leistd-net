using Leistd.Auditing.Abstractions;
using Leistd.MultiTenancy.Tenancy;
using Leistd.Webhooks.Management;

namespace Leistd.Webhooks.EntityFrameworkCore.Entities;

/// <summary>一个 webhook 订阅：把哪些事件类型推到哪个地址。</summary>
/// <remarks>
/// 实现 <see cref="IMultiTenant"/>：按租户隔离，宿主行只接收宿主上下文发布的事件。
/// 映射进继承 <c>BaseDbContext</c> 的上下文时，查询过滤由基座接管。
/// </remarks>
public class WebhookSubscription : ICreationAuditedObject, IModificationAuditedObject, IMultiTenant
{
    /// <summary>订阅 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>名称。</summary>
    public string Name { get; set; } = default!;

    /// <summary>说明（对接方与用途）；没有时为 <see langword="null"/>。</summary>
    public string? Description { get; set; }

    /// <summary>目标地址，绝对 http(s) URL。</summary>
    public string Url { get; set; } = default!;

    /// <summary>订阅的事件类型名；只含 <see cref="Definitions.WebhookDefinition.AllEventTypes"/> 时表示全部事件。</summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>自定义请求头。</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>当前签名密钥的 Data Protection 密文。</summary>
    public string Secret { get; set; } = default!;

    /// <summary>上一把签名密钥的密文；不在轮换过渡期时为 <see langword="null"/>。</summary>
    public string? PreviousSecret { get; set; }

    /// <summary>上一把密钥继续签名的截止时刻（UTC）。</summary>
    public DateTime? PreviousSecretExpiresAt { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>停用原因；启用时为 <see langword="null"/>。</summary>
    public WebhookDisabledReason? DisabledReason { get; set; }

    /// <summary>停用时刻（UTC）。</summary>
    public DateTime? DisabledAt { get; set; }

    /// <summary>本轮连续失败的起点（UTC）；任何一次成功或重新启用时清空。</summary>
    public DateTime? FirstFailureAt { get; set; }

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public string? LastModifierId { get; set; }
}
