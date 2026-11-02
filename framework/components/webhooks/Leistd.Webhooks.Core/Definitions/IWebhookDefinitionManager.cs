namespace Leistd.Webhooks.Definitions;

/// <summary>提供已登记事件类型的只读索引。</summary>
/// <remarks>聚合全部 <see cref="IWebhookDefinitionProvider"/> 的登记结果，含内置的 <see cref="WebhookDefinition.TestEventType"/>。</remarks>
public interface IWebhookDefinitionManager
{
    /// <summary>按类型名查找；不存在时返回 <see langword="null"/>。</summary>
    WebhookDefinition? GetOrNull(string name);

    /// <summary>获取全部定义，按登记顺序。</summary>
    IReadOnlyList<WebhookDefinition> GetAll();
}
