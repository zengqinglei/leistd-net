namespace Leistd.Webhooks.AspNetCore.Endpoints;

/// <summary>webhook 管理端点的授权口径，全部必填。</summary>
/// <remarks>组件不内置默认策略，漏配任何一项时 <c>MapWebhooks</c> 映射即抛出。</remarks>
public sealed class WebhookEndpointOptions
{
    /// <summary>查看事件类型、订阅与投递日志所需的授权策略名。</summary>
    public string ReadPolicy { get; set; } = string.Empty;

    /// <summary>新建、编辑、删除、启停订阅，轮换密钥，发送测试事件与手动重发所需的授权策略名。</summary>
    public string ManagePolicy { get; set; } = string.Empty;

    // 缺必填项抛 ArgumentException，ParamName 即属性名
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManagePolicy);
    }
}
