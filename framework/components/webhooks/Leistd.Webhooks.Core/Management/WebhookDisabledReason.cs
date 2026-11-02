namespace Leistd.Webhooks.Management;

/// <summary>订阅停用的原因。</summary>
/// <remarks>JSON 里的写法由宿主的枚举转换器决定；模板配置为 camelCase 名称（<c>manual</c>、<c>endpointGone</c>、<c>consecutiveFailures</c>）。</remarks>
public enum WebhookDisabledReason
{
    /// <summary>管理员手动停用。</summary>
    Manual,

    /// <summary>端点已注销：接收方回了 410。</summary>
    EndpointGone,

    /// <summary>连续失败：从首次失败起超过 <c>Leistd:Webhooks:AutoDisableAfter</c> 仍未成功。</summary>
    ConsecutiveFailures
}
