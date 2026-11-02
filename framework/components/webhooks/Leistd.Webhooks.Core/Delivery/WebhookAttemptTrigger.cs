namespace Leistd.Webhooks.Delivery;

/// <summary>一次尝试的触发来源。</summary>
/// <remarks>JSON 里的写法由宿主的枚举转换器决定；模板配置为 camelCase 名称（<c>automatic</c>、<c>manual</c>）。</remarks>
public enum WebhookAttemptTrigger
{
    /// <summary>自动：首发或按退避表排期的重试。</summary>
    Automatic,

    /// <summary>手动：管理员重发，或重新启用订阅时选择重发失败投递；只发一次，失败不重开自动退避。</summary>
    Manual
}
