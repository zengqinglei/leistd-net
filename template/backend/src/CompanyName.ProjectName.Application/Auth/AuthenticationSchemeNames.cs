namespace CompanyName.ProjectName.Application.Auth;

/// <summary>
/// 认证方案名
/// </summary>
/// <remarks>
/// <para>方案名同时被三处使用：<c>Program.cs</c> 注册与授权策略、Controller 的
/// <c>SignInAsync</c> / <c>SignOutAsync</c>、以及应用层构造会话主体时的
/// <c>ClaimsIdentity</c> 认证类型。三处必须完全一致——改错一处的表现是
/// "登录成功但后续请求全是匿名"，没有任何编译期信号。</para>
/// <para>常量放在应用层而不是 Api 层：应用层构造主体时需要它，而应用层不能引用 Api。
/// 与 <c>TenantConnectionScopes</c> 同型。</para>
/// <para>刻意不叫 <c>AuthenticationSchemes</c>：那个名字与 <c>System.Net.AuthenticationSchemes</c>
/// 冲突，而 <c>Program.cs</c> 有 <c>using System.Net;</c>——撞名只在生成产物里才报错。</para>
/// </remarks>
public static class AuthenticationSchemeNames
{
    /// <summary>Cookie 会话方案</summary>
    public const string SessionCookie = "MyProjectCookie";
}
