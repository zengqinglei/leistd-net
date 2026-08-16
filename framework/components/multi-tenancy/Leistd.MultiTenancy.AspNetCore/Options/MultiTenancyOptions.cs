using Leistd.Security.Claims;

namespace Leistd.MultiTenancy;

/// <summary>
/// Web 宿主多租户配置
/// </summary>
public class MultiTenancyOptions
{
    /// <summary>
    /// 承载租户线索的请求头名。默认 <c>X-Tenant-Id</c>，
    /// 与 <c>Leistd.ServiceClient</c> 出站注入的租户头默认值一致
    /// </summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// 承载租户线索的查询串参数名（用于邮件链接等匿名链路）。默认 <c>tenant</c>
    /// </summary>
    public string QueryStringParameterName { get; set; } = "tenant";

    /// <summary>
    /// 认证主体中的租户 claim 类型。默认 <see cref="CustomClaimTypes.TenantId"/>（<c>tenant_id</c>）
    /// </summary>
    public string TenantClaimType { get; set; } = CustomClaimTypes.TenantId;

    /// <summary>
    /// 子域名解析格式，形如 <c>{0}.example.com</c>；为空（默认）则不启用子域名解析
    /// </summary>
    /// <remarks>
    /// <para>配置后 <c>acme.example.com</c> 解析为租户 <c>acme</c>，登录页不再需要租户输入框——
    /// 这是 SaaS 的主流形态，便利性与安全性同时最优：用户什么都不用记，
    /// 而系统也不需要任何"列出全部租户"的接口。</para>
    /// <para>只写主机名，不带端口：端口属于部署形态，写进格式会让开发与生产各配一份。</para>
    /// <para><b>只把真正用于租户的通配子域指向本应用</b>。把整个顶级域通配过来时，
    /// <c>www.example.com</c> 会被解析成名为 <c>www</c> 的租户并因查不到而 404。</para>
    /// </remarks>
    public string? DomainFormat { get; set; }
}
