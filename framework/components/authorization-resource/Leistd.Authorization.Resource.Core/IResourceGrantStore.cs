namespace Leistd.Authorization.Resource;

/// <summary>
/// 单条资源 ACL 授予。
/// </summary>
/// <param name="Operation">被授予的操作。</param>
/// <param name="ProviderName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
/// <param name="ProviderKey">授予对象 Key。</param>
/// <param name="Effect">授予效果。</param>
public sealed record ResourceGrant(
    string Operation,
    string ProviderName,
    string ProviderKey,
    PermissionGrantEffect Effect);

/// <summary>
/// 资源 ACL 存储（只读）。
/// </summary>
/// <remarks>
/// 除单实例判定外，还必须提供集合级入口
/// <see cref="QueryGrantedResourceKeys"/>：列表、统计和导出不能先加载候选再逐条判定，
/// 否则分页总数、排序和性能都会错误。该方法返回可被数据库翻译的
/// <see cref="IQueryable{T}"/>，调用方以 <c>Contains</c> 的形式把它合并进业务查询，
/// 数据库据此生成 EXISTS/IN 子查询。
/// </remarks>
public interface IResourceGrantStore
{
    /// <summary>
    /// 获取某个资源实例上的全部 ACL 授予。
    /// </summary>
    Task<IReadOnlyList<ResourceGrant>> GetGrantsAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定主体在某个资源实例上对每个操作的最终效果（拒绝优先）。
    /// </summary>
    Task<IReadOnlyDictionary<string, PermissionGrantEffect>> GetEffectiveGrantsAsync(
        string resourceName,
        string resourceKey,
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构造"指定主体被授予某操作的资源 Key"查询，用于把 ACL 合并进集合查询。
    /// </summary>
    /// <remarks>
    /// 返回的查询已排除存在显式拒绝的资源 Key。调用方典型用法：
    /// <code>
    /// var keys = store.QueryGrantedResourceKeys("Orders", ResourceOperations.Read, userId, roleIds);
    /// query = query.Where(order => keys.Contains(order.ResourceKey));
    /// </code>
    /// 为保证可翻译，业务实体应当持有与 ACL 一致的字符串资源 Key 列。
    /// </remarks>
    IQueryable<string> QueryGrantedResourceKeys(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds);
}

/// <summary>
/// 资源 ACL 管理器（写入）。
/// </summary>
public interface IResourceGrantManager
{
    /// <summary>
    /// 原子替换某个资源实例上的全部 ACL。
    /// </summary>
    Task ReplaceGrantsAsync(
        string resourceName,
        string resourceKey,
        IReadOnlyCollection<ResourceGrant> grants,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理某个资源实例的全部 ACL。资源被删除后调用；重复调用是幂等的。
    /// </summary>
    /// <remarks>
    /// 通用 ACL 表无法对任意业务表建立外键，因此除了在删除资源时调用本方法，
    /// 还应安排周期性的孤儿记录清理。
    /// </remarks>
    Task<int> RemoveResourceAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default);
}
