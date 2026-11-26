namespace CompanyName.ProjectName.Api.Options;

/// <summary>会话 Cookie 的站点策略与时长（配置节 <c>SessionCookie</c>）。</summary>
/// <remarks>
/// <para>默认沿用 Cookie 认证的 <c>Lax</c>，浏览器页面与所属 API 同源。</para>
/// <para>第三方站点以顶层 POST 进入授权或退出端点时，如需附带会话可评估 <c>None</c>，
/// 并落实请求来源防护；模板未启用 antiforgery。此设置不补齐跨源浏览器认证导航。</para>
/// <para>短时外部票据 Cookie 读同一个值；协议 correlation/nonce Cookie 保持官方默认 None + Secure。</para>
/// </remarks>
public sealed class SessionCookieOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "SessionCookie";

    /// <summary>站点策略；<see langword="null"/> 表示使用 Cookie 认证默认值 <c>Lax</c></summary>
    public SameSiteMode? SameSite { get; set; }

#if (RemoteTokenAuth)
    /// <summary>资源服务本地票据的时长（天）。</summary>
    public int ExpireDays { get; set; } = 30;

    /// <summary>访问令牌到期后，签发方不可达时允许沿用会话主体的时长，默认 1 小时。</summary>
    public TimeSpan RefreshGracePeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>本地票据有效时长。</summary>
    public TimeSpan Lifetime => TimeSpan.FromDays(ExpireDays);
#endif
}
