using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 租户的数据放置方式。
/// </summary>
public enum TenantDatabaseMode
{
    /// <summary>使用服务宿主配置的命名连接，租户数据由 <see cref="IMultiTenant.TenantId"/> 隔离。</summary>
    SharedDatabase = 0,

    /// <summary>使用租户覆盖连接，各服务在同一目标数据库中使用自己的固定 schema。</summary>
    DedicatedDatabase = 1
}
