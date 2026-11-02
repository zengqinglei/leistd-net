namespace Leistd.Webhooks.Errors;

/// <summary>webhook 组件发出的错误码，兼本地化资源键；默认中英文案随包分发。</summary>
public static class WebhookErrorCodes
{
    /// <summary>订阅不存在。</summary>
    public const string SubscriptionNotFound = "Webhook:SubscriptionNotFound";

    /// <summary>投递不存在。</summary>
    public const string DeliveryNotFound = "Webhook:DeliveryNotFound";

    /// <summary>订阅已停用，不能发送测试事件或手动重发。</summary>
    public const string SubscriptionDisabled = "Webhook:SubscriptionDisabled";

    /// <summary>投递仍在重试中，不能手动重发。</summary>
    public const string DeliveryNotRetryable = "Webhook:DeliveryNotRetryable";
}
