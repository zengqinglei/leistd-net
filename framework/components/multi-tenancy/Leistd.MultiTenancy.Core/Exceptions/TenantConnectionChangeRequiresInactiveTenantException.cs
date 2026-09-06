using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示尝试修改启用中租户的连接配置。
/// </summary>
/// <remarks>
/// 修改已有配置前必须停用并排空租户，避免热实例与冷实例写入不同物理库。首次创建不受此限制。
/// </remarks>
/// <param name="tenantId">目标租户</param>
public class TenantConnectionChangeRequiresInactiveTenantException(Guid tenantId)
    : ConflictException(
        $"Tenant '{tenantId}' must be deactivated before its connection configuration can be changed. " +
        "Changing the route while the tenant is serving lets cached and cold instances write to " +
        "different physical databases at the same time. Deactivate the tenant, wait for both the " +
        "access token lifetime and the route cache TTL to elapse, migrate the data, then change the route.")
{
    /// <summary>获取目标租户标识。</summary>
    public Guid TenantId { get; } = tenantId;
}
