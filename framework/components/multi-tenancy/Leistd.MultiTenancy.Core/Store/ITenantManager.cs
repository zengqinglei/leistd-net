namespace Leistd.MultiTenancy;

/// <summary>
/// 租户管理器：租户注册表的唯一写入口，负责名称归一化、唯一性校验与存储缓存失效
/// </summary>
/// <remarks>
/// 契约与出参都在 Core：应用层依赖它即可完成租户管理，不必引用任何持久化实现包。
/// EF 实现见 <c>Leistd.MultiTenancy.EntityFrameworkCore</c> 的 <c>AddMultiTenancyEfCore&lt;TDbContext&gt;()</c>。
/// </remarks>
public interface ITenantManager
{
    /// <summary>
    /// 创建租户。名称在未删除租户中大小写不敏感唯一，冲突抛 <see cref="DuplicateTenantNameException"/>
    /// </summary>
    Task<TenantConfiguration> CreateAsync(string name, string? displayName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新租户名称与显示名。名称冲突抛 <see cref="DuplicateTenantNameException"/>
    /// </summary>
    Task<TenantConfiguration> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/停用租户。停用后该租户的请求自下一次校验起被拒绝
    /// </summary>
    Task<TenantConfiguration> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除租户（软删除）。业务数据保留但自此不可达
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 Id 查找租户（管理读路径，不经存储缓存），不存在或已删除返回 null
    /// </summary>
    Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询租户（管理界面读路径）：名称/显示名关键字过滤，按创建时间倒序
    /// </summary>
    Task<TenantPage> GetPagedAsync(string? keyword, int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// 租户分页结果
/// </summary>
/// <param name="TotalCount">符合条件的总数</param>
/// <param name="Items">当前页租户</param>
public sealed record TenantPage(long TotalCount, IReadOnlyList<TenantConfiguration> Items);
