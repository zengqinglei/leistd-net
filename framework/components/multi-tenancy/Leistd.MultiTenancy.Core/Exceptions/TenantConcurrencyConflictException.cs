using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户生命周期发生并发写入冲突。
/// </summary>
/// <remarks>
/// 启用、停用、改名与连接配置共用租户版本，防止并发改路由导致同一租户同时写入不同数据库。
/// </remarks>
public class TenantConcurrencyConflictException : BusinessException
{
    /// <summary>构造异常。</summary>
    /// <param name="tenantId">目标租户</param>
    public TenantConcurrencyConflictException(Guid tenantId)
        : base(MultiTenancyErrorCodes.ConcurrencyConflict,
            $"Tenant '{tenantId}' was modified concurrently. Activation, deactivation, renaming and " +
            "connection-configuration changes all contend for the same tenant version. Re-read the " +
            "tenant state and decide whether to retry.")
    {
        TenantId = tenantId;
    }

    /// <summary>获取目标租户标识。</summary>
    public Guid TenantId { get; }
}
