namespace Leistd.MultiTenancy;

/// <summary>
/// 从租户控制数据库读取连接配置。缺失配置返回 <see langword="null"/>，调用方必须失败关闭。
/// </summary>
public interface ITenantConnectionConfigurationStore
{
    /// <summary>按租户 Id 读取连接配置。</summary>
    Task<TenantConnectionConfiguration?> FindAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 枚举全部未删除租户的连接配置，供单服务 DbMigrator 计算物理迁移目标。
    /// </summary>
    Task<IReadOnlyList<TenantConnectionConfiguration>> GetListAsync(
        CancellationToken cancellationToken = default);
}
