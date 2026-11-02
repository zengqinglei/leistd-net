using Leistd.EventBus.Events;
using Leistd.Webhooks.Management;

namespace Leistd.Webhooks.Events;

/// <summary>一个 webhook 订阅被创建、轮换密钥、启用、停用或删除。</summary>
/// <remarks>
/// <para>经可选的 <c>ILocalEventBus</c> 发布：在工作单元内推迟到提交后分发，写入失败或回滚时不分发；
/// 处于订阅所属租户的上下文；未注册事件总线时不发布。</para>
/// <para>管理用例在状态真正改变时发布：对已启用的订阅再启用、对已停用的再停用不发布。
/// 停用也可能来自投递链路（端点已注销、连续失败），此时 <see cref="DisabledReason"/> 不是
/// <see cref="WebhookDisabledReason.Manual"/>；宿主据此区分"谁的操作"与"需要告知管理员的自动停用"。</para>
/// <para>只改名称、地址、事件类型或请求头不发布。事件不带密钥。</para>
/// </remarks>
/// <param name="subscriptionId">订阅标识。</param>
/// <param name="subscriptionName">订阅名快照；删除时取删除前的值。</param>
/// <param name="change">变更类别。</param>
/// <param name="disabledReason">停用原因，仅 <see cref="WebhookSubscriptionChangeKind.Disabled"/> 时有值。</param>
public sealed class WebhookSubscriptionChangedEvent(
    Guid subscriptionId,
    string subscriptionName,
    WebhookSubscriptionChangeKind change,
    WebhookDisabledReason? disabledReason = null) : LocalEvent
{
    /// <summary>订阅标识。</summary>
    public Guid SubscriptionId { get; } = subscriptionId;

    /// <summary>订阅名快照。</summary>
    public string SubscriptionName { get; } = subscriptionName;

    /// <summary>变更类别。</summary>
    public WebhookSubscriptionChangeKind Change { get; } = change;

    /// <summary>停用原因；其它变更类别为 <see langword="null"/>。</summary>
    public WebhookDisabledReason? DisabledReason { get; } = disabledReason;
}

/// <summary>订阅变更类别。</summary>
public enum WebhookSubscriptionChangeKind
{
    /// <summary>创建。</summary>
    Created,

    /// <summary>轮换签名密钥。</summary>
    SecretRotated,

    /// <summary>由停用变为启用。</summary>
    Enabled,

    /// <summary>由启用变为停用（手动或自动，见 <see cref="WebhookSubscriptionChangedEvent.DisabledReason"/>）。</summary>
    Disabled,

    /// <summary>删除，连同它的投递记录。</summary>
    Deleted
}
