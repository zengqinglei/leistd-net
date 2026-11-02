#if (LocalIdentity)
using Leistd.Webhooks.Definitions;

namespace CompanyName.ProjectName.Application.Webhooks.Provider;

/// <summary>登记本项目可对外发布的 webhook 事件类型。</summary>
/// <remarks>
/// <para>类型名是接收方按类型分支的依据，一经对外发布就是契约：语义或载荷结构要变时登记新类型名，不改旧的。</para>
/// <para>分组驱动后台订阅对话框的分组显示；内置的 <c>webhook.test</c> 由组件登记，这里不重复。</para>
/// </remarks>
public class WebhookDefinitionProvider : IWebhookDefinitionProvider
{
    /// <summary>用户已创建，载荷 <c>{ userId }</c>。</summary>
    public const string UserCreated = "user.created";

    public void Define(IWebhookDefinitionContext context)
    {
        context.Add(UserCreated, "users");
    }
}
#endif
