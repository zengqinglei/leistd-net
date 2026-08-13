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
}
