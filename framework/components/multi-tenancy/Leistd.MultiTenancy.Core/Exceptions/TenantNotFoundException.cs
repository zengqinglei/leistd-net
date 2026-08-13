using Leistd.Exception.Core;

namespace Leistd.MultiTenancy;

/// <summary>
/// 租户不存在异常。继承 <see cref="NotFoundException"/>，经全局异常处理器映射为 HTTP 404
/// </summary>
/// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
public class TenantNotFoundException(string tenantIdOrName)
    : NotFoundException($"Tenant not found: {tenantIdOrName}")
{
    /// <summary>
    /// 解析出的租户线索（Id 或名称）
    /// </summary>
    public string TenantIdOrName { get; } = tenantIdOrName;
}
