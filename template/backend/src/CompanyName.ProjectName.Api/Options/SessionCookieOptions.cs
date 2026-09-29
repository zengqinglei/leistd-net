#if (LocalIdentity)
namespace CompanyName.ProjectName.Api.Options;

/// <summary>
/// 会话 Cookie 的站点策略与时长（配置节 <c>SessionCookie</c>）
/// </summary>
/// <remarks>
/// <para>默认不设置，沿用 Cookie 认证的 <c>Lax</c>：同源部署以及同站的前后端分离
/// （如 <c>app.example.com</c> 调 <c>api.example.com</c>，按公共后缀判定为同站）都可用。
/// 托管公共后缀域（如 <c>*.azurewebsites.net</c>）下的两个子域属于跨站。</para>
/// <para>需要在跨站请求上携带会话 Cookie 时才设为 <c>None</c>（跨站部署，或见部署说明列出的跨站 POST 情形），此时浏览器会在第三方页面发起的请求上带会话 Cookie，
/// 部署方须自行接入防伪令牌：模板未启用 antiforgery，Angular 内置的 XSRF 只对同源相对地址生效。</para>
/// <para>外部登录的状态 Cookie 读同一个值，两者策略不一致时回调拿不到状态。</para>
/// </remarks>
public sealed class SessionCookieOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "SessionCookie";

    /// <summary>站点策略；<see langword="null"/> 表示使用 Cookie 认证默认值 <c>Lax</c></summary>
    public SameSiteMode? SameSite { get; set; }

    /// <summary>会话时长（天）：会话 Cookie 的滑动过期与服务端会话的空闲时限取同一个值，至少 1 天</summary>
    public int ExpireDays { get; set; } = 7;
}
#endif
