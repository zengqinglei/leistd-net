namespace Leistd.Webhooks.Definitions;

/// <summary>向定义上下文登记本模块可对外发布的 webhook 事件类型。</summary>
/// <remarks>
/// <para>发布器拒绝未登记的类型；订阅只能选择已登记的类型。</para>
/// <para>类型名一经发布不要修改：它是接收方按类型分支的依据，也是已存事件的含义本身。</para>
/// </remarks>
/// <example>
/// <code>
/// public class AppWebhookDefinitionProvider : IWebhookDefinitionProvider
/// {
///     public void Define(IWebhookDefinitionContext context)
///     {
///         context.Add("order.paid", "orders", "Webhooks:Events:OrderPaid");
///     }
/// }
///
/// builder.Services.AddSingleton&lt;IWebhookDefinitionProvider, AppWebhookDefinitionProvider&gt;();
/// </code>
/// </example>
public interface IWebhookDefinitionProvider
{
    /// <summary>登记事件类型定义。</summary>
    /// <param name="context">定义上下文。</param>
    void Define(IWebhookDefinitionContext context);
}

/// <summary>事件类型的登记与查询上下文。</summary>
/// <remarks>类型名全局唯一，重复登记在启动期抛出。</remarks>
public interface IWebhookDefinitionContext
{
    /// <summary>登记一个事件类型。</summary>
    /// <param name="name">类型名，全局唯一且一经发布不可更改（如 <c>order.paid</c>）。</param>
    /// <param name="group">分组，驱动界面按组展示；取值由业务定义（如 <c>"orders"</c>），框架不预置清单。</param>
    /// <param name="description">说明，通常是本地化键；组件不翻译，原样下发。</param>
    WebhookDefinition Add(string name, string group, string? description = null);

    /// <summary>按类型名查找定义；不存在时返回 <see langword="null"/>。</summary>
    WebhookDefinition? GetOrNull(string name);
}
