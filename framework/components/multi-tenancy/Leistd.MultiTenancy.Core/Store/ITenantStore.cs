namespace Leistd.MultiTenancy;

/// <summary>
/// 租户配置存储：中间件校验租户与登录前租户探测的数据源
/// </summary>
/// <remarks>
/// 框架不默认注册实现：持有租户注册表的服务使用
/// <c>Leistd.MultiTenancy.EntityFrameworkCore</c> 的 EF 实现；
/// 仅消费 token 中租户 claim 的资源服务可用 <see cref="InMemoryTenantStore"/>（配置型）。
/// </remarks>
public interface ITenantStore
{
    /// <summary>
    /// 按 Id 查找租户，不存在返回 null
    /// </summary>
    Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按归一化名称查找租户，不存在返回 null
    /// </summary>
    Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default);
}
