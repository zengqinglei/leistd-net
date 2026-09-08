using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.Stores;

/// <summary>
/// 管理租户注册表并规范化租户名称。
/// </summary>
/// <remarks>
/// 契约与出参都在 Core：应用层依赖它即可完成租户管理，不必引用任何持久化实现包。
/// EF 实现见 <c>Leistd.MultiTenancy.EntityFrameworkCore</c> 的 <c>AddMultiTenancyEfCore&lt;TDbContext&gt;()</c>。
/// </remarks>
public interface ITenantManager
{
    /// <summary>
    /// 创建租户，名称在未删除租户中大小写不敏感且唯一。
    /// </summary>
    /// <param name="name">租户名称，大小写不敏感唯一；归一化后落库</param>
    /// <param name="displayName">展示名，可为 <c>null</c></param>
    /// <param name="isActive">
    /// 初始是否启用。需要继续初始化角色、管理员等数据时传 <c>false</c>，
    /// 初始化完成后用 <see cref="SetActiveAsync"/> 激活。
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
    /// 更新租户名称和显示名称。
    /// </summary>
    /// <returns>更新后的租户配置</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或全空白</exception>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
    /// <exception cref="DuplicateTenantNameException">新名称已被其它未删除租户占用</exception>
    Task<TenantConfiguration> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更改租户启用状态。
    /// </summary>
    /// <remarks>
    /// 停用在提交那一刻对所有节点生效：<c>EfCoreTenantStore</c> 直接读库，
    /// 不存在"已提交但仍被放行"的窗口。
    /// </remarks>
    /// <returns>更新后的租户配置</returns>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
    Task<TenantConfiguration> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// 软删除租户并保留其业务数据。
    /// </summary>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除</exception>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识查找未删除租户。
    /// </summary>
    Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称关键字分页查询租户，并按创建时间倒序返回。
    /// </summary>
    Task<TenantPage> GetPagedAsync(string? keyword, int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// 表示租户分页结果。
/// </summary>
/// <param name="TotalCount">符合条件的总数</param>
/// <param name="Items">当前页租户</param>
public sealed record TenantPage(long TotalCount, IReadOnlyList<TenantConfiguration> Items);
