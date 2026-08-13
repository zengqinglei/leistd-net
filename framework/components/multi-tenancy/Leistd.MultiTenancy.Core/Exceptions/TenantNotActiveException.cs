using Leistd.Exception.Core;

namespace Leistd.MultiTenancy;

/// <summary>
/// 租户已停用异常。继承 <see cref="ForbiddenException"/>，经全局异常处理器映射为 HTTP 403
/// </summary>
/// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
public class TenantNotActiveException(string tenantIdOrName)
    : ForbiddenException($"Tenant is not active: {tenantIdOrName}")
{
    /// <summary>
    /// 解析出的租户线索（Id 或名称）
    /// </summary>
    public string TenantIdOrName { get; } = tenantIdOrName;
}
