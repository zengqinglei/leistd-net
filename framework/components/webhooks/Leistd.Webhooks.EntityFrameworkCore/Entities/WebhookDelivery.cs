using Leistd.MultiTenancy.Tenancy;
using Leistd.Webhooks.Delivery;

namespace Leistd.Webhooks.EntityFrameworkCore.Entities;

/// <summary>一个事件发给一个订阅的投递及其当前进度。</summary>
/// <remarks>实现 <see cref="IMultiTenant"/>：与事件、订阅同属一个租户。</remarks>
public class WebhookDelivery : IMultiTenant
{
    /// <summary>投递 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>事件 ID。</summary>
    public Guid EventId { get; set; }

    /// <summary>订阅 ID。</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>状态。</summary>
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    /// <summary>下一次尝试的触发来源，与排期同事务保存；尝试结束后复位为自动。</summary>
    public WebhookAttemptTrigger NextTrigger { get; set; } = WebhookAttemptTrigger.Automatic;

    /// <summary>累计尝试次数，含手动尝试。</summary>
    public int AttemptCount { get; set; }

    /// <summary>本轮自动尝试数，决定退避表下标；手动尝试不计入。</summary>
    public int AutoAttemptCount { get; set; }

    /// <summary>下一次尝试的时刻（UTC）；终态时为 <see langword="null"/>。</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>最近一次尝试的状态码；没有收到响应时为 <see langword="null"/>。</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>最近一次没有收到响应的原因，或订阅停用时终结投递的原因。</summary>
    public string? LastError { get; set; }

    /// <summary>最近一次尝试的时刻（UTC）。</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>创建时间（UTC），即事件发布时刻；统计与保留期按它计。</summary>
    public DateTime CreationTime { get; set; }
}
