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
    /// 创建租户。名称在未删除租户中大小写不敏感唯一
    /// </summary>
    /// <param name="name">租户名称，大小写不敏感唯一；归一化后落库</param>
    /// <param name="displayName">展示名，可为 <c>null</c></param>
    /// <param name="isActive">
    /// 初始是否启用。**故意不给默认值**：这是安全相关的选择，最危险的取值不该是最省事的写法。
    /// 需要在创建后继续初始化租户数据（角色、管理员等）时必须传 <c>false</c>——
    /// 租户一旦启用，中间件就会接受它，匿名入口（注册等）能进入一个还没有管理员的半成品租户；
    /// 初始化完成后再用 <see cref="SetActiveAsync"/> 激活。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建出的租户配置</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或全空白</exception>
    /// <exception cref="DuplicateTenantNameException">名称已被未删除的租户占用（含并发落败）</exception>
    Task<TenantConfiguration> CreateAsync(
        string name,
        string? displayName,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新租户名称与显示名
    /// </summary>
    /// <returns>更新后的租户配置</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或全空白</exception>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
    /// <exception cref="DuplicateTenantNameException">新名称已被其它未删除租户占用</exception>
    Task<TenantConfiguration> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/停用租户
    /// </summary>
    /// <remarks>
    /// 写入后即失效存储缓存，停用最迟在缓存条目的绝对过期窗口内对所有节点生效
    /// （见 <c>EfCoreTenantStore</c> 的过期策略）。失效本身失败时本方法抛出——
    /// 库中状态已提交，重试即自愈。
    /// </remarks>
    /// <returns>更新后的租户配置</returns>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
    Task<TenantConfiguration> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除租户（软删除）。业务数据保留但自此不可达
    /// </summary>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
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
