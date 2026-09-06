using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户不存在。
/// </summary>
/// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
public class TenantNotFoundException(string tenantIdOrName)
    : NotFoundException($"Tenant not found: {tenantIdOrName}")
{
    /// <summary>
    /// 获取解析出的租户标识或名称。
    /// </summary>
    public string TenantIdOrName { get; } = tenantIdOrName;
}
