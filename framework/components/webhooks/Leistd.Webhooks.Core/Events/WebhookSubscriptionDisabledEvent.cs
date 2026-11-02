using Leistd.EventBus.Events;
using Leistd.Webhooks.Management;

namespace Leistd.Webhooks.Events;

/// <summary>一个 webhook 订阅被停用（手动、端点已注销或连续失败）。</summary>
/// <remarks>
/// <para>经可选的 <c>ILocalEventBus</c> 在停用提交后分发，处于订阅所属租户的上下文；未注册事件总线时不发布。</para>
/// <para>组件不据此通知任何人，宿主按 <see cref="Reason"/> 决定是否告知管理员。</para>
/// </remarks>
/// <param name="subscriptionId">订阅标识。</param>
/// <param name="subscriptionName">订阅名快照。</param>
/// <param name="reason">停用原因。</param>
public sealed class WebhookSubscriptionDisabledEvent(Guid subscriptionId, string subscriptionName, WebhookDisabledReason reason) : LocalEvent
{
    /// <summary>订阅标识。</summary>
    public Guid SubscriptionId { get; } = subscriptionId;

    /// <summary>订阅名快照。</summary>
    public string SubscriptionName { get; } = subscriptionName;

    /// <summary>停用原因。</summary>
    public WebhookDisabledReason Reason { get; } = reason;
}
