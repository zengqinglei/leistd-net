using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户已停用。
/// </summary>
/// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
public class TenantNotActiveException(string tenantIdOrName)
    : ForbiddenException($"Tenant is not active: {tenantIdOrName}")
{
    /// <summary>
    /// 获取解析出的租户标识或名称。
    /// </summary>
    public string TenantIdOrName { get; } = tenantIdOrName;
}
