using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户生命周期发生并发写入冲突。
/// </summary>
/// <remarks>
/// 启用、停用、改名与连接配置共用租户版本，防止并发改路由导致同一租户同时写入不同数据库。
/// </remarks>
/// <param name="tenantId">目标租户</param>
public class TenantConcurrencyConflictException(Guid tenantId)
    : ConflictException(
        $"Tenant '{tenantId}' was modified concurrently. Activation, deactivation, renaming and " +
        "connection-configuration changes all contend for the same tenant version. Re-read the " +
        "tenant state and decide whether to retry.")
{
    /// <summary>获取目标租户标识。</summary>
    public Guid TenantId { get; } = tenantId;
}
