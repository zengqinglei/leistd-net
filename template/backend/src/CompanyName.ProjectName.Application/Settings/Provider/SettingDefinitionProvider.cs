using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;

namespace CompanyName.ProjectName.Application.Settings.Provider;

/// <summary>
/// 设置定义提供器
/// </summary>
/// <remarks>
/// 这里只声明<b>运行期可改</b>的业务偏好。连接串、密钥、认证协议等部署期配置一律留在
/// <c>appsettings</c> 与 <c>IOptions&lt;T&gt;</c>：把它们搬进设置表等于给运行期一个能悄悄
/// 拆掉安全与正确性保证的开关。
/// <para>已经是某个实体字段的东西也不要再定义成设置——例如租户显示名归
/// <c>TenantRecord.DisplayName</c>，在租户管理里改；两处都能写就有两个互相矛盾的事实源。</para>
/// <para>displayName 存的是<b>人类可读文案</b>，不是本地化键：关掉本地化的项目里没有翻译器，
/// 存键会让界面直接显示 <c>Setting:Display.Language</c> 这样的内部标识。
/// 本地化项目按 <c>Setting:{name}</c> 查词条翻译，查不到才回落到这里的文案——
/// 键由名称推导，不必在定义里再抄一遍。</para>
/// </remarks>
public class SettingDefinitionProvider : ISettingDefinitionProvider
{
    /// <inheritdoc />
    public void Define(ISettingDefinitionContext context)
    {
#if (IncludeLocalization)
        // 界面语言：租户给默认值，用户可各自覆盖。只有装了本地化才有多种语言可选，
        // 因此这一项随本地化一起裁剪——单语言应用里让人挑语言是个假开关。
        context.Add(
            SettingConstant.Display.Language,
            // 与前端的 DEFAULT_LANG 和 transloco 的 defaultLang 保持一致：
            // 这里写反会让用户在登录页看到英文、进入首页后被切成另一种语言。
            defaultValue: "en",
            scopes: SettingScopes.All,
            displayName: "Language").IsVisibleToClients = true;

#endif
        context.Add(
            SettingConstant.Display.TimeZone,
            defaultValue: "Asia/Shanghai",
            scopes: SettingScopes.All,
            displayName: "Time zone").IsVisibleToClients = true;
    }
}
