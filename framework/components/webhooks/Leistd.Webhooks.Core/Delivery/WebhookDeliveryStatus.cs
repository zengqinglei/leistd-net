namespace Leistd.Webhooks.Delivery;

/// <summary>投递状态。</summary>
/// <remarks>JSON 里的写法由宿主的枚举转换器决定；模板配置为 camelCase 名称（<c>pending</c>、<c>succeeded</c>、<c>failed</c>）。</remarks>
public enum WebhookDeliveryStatus
{
    /// <summary>待投递：等待首发或下一次重试（界面显示为"重试中"）。</summary>
    Pending,

    /// <summary>成功：最近一次尝试收到 2xx。</summary>
    Succeeded,

    /// <summary>失败：重试用完、手动尝试失败、端点已注销或订阅停用时被终结。</summary>
    Failed
}
