using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户不存在。
/// </summary>
public class TenantNotFoundException : NotFoundException
{
    /// <summary>构造异常。</summary>
    /// <param name="tenantIdOrName">解析出的租户线索（Id 或名称）</param>
    public TenantNotFoundException(string tenantIdOrName)
        : base($"Tenant not found: {tenantIdOrName}")
    {
        TenantIdOrName = tenantIdOrName;
        WithCode(MultiTenancyErrorCodes.NotFound);
    }

    /// <summary>
    /// 获取解析出的租户标识或名称。
    /// </summary>
    public string TenantIdOrName { get; }
}
