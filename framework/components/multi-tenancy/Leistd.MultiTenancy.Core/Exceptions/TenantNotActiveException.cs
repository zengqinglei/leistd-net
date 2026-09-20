using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户已停用。
/// </summary>
public class TenantNotActiveException : ForbiddenException
{
    /// <summary>构造异常。</summary>
    /// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
    public TenantNotActiveException(string tenantIdOrName)
        : base($"Tenant is not active: {tenantIdOrName}")
    {
        TenantIdOrName = tenantIdOrName;
        WithCode(MultiTenancyErrorCodes.NotActive);
    }

    /// <summary>
    /// 获取解析出的租户标识或名称。
    /// </summary>
    public string TenantIdOrName { get; }
}
