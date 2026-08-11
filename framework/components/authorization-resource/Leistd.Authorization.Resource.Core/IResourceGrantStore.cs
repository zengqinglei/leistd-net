namespace Leistd.Authorization.Resource;

/// <summary>
/// 资源实例 ACL 的授予效果。
/// </summary>
/// <remarks>
/// 与功能权限不同，资源共享保留"显式拒绝"：把一份资源共享给一个部门、同时排除其中某个人，
/// 没有等价的替代写法（只能改成逐个列举个人，随人员变动即刻失效）。
/// 功能权限的"减法"可以靠拆分角色解决，因此那一层是纯加法；两者取舍依据不同，故各自定义。
/// </remarks>
public enum ResourceGrantEffect
{
    /// <summary>显式允许。</summary>
    Granted = 1,

    /// <summary>显式拒绝。优先于任何来源的允许。</summary>
    Prohibited = 2
}

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
    ResourceGrantEffect Effect);

/// <summary>
/// 某个资源实例的完整 ACL 与其版本。
/// </summary>
/// <param name="ResourceName">资源类型名。</param>
/// <param name="ResourceKey">资源实例 Key。</param>
/// <param name="Grants">该实例上的全部授予。</param>
/// <param name="Revision">ACL 版本，尚未写入过时为 0。保存时回传即可获得乐观并发保护。</param>
public sealed record ResourceGrantSet(
    string ResourceName,
    string ResourceKey,
    IReadOnlyList<ResourceGrant> Grants,
    long Revision);

/// <summary>
/// 资源 ACL 版本冲突。调用方应重新加载后再保存，宿主通常映射为 HTTP 409。
/// </summary>
public sealed class ResourceGrantConcurrencyException(
    string resourceName,
    string resourceKey,
    long expectedRevision,
    long actualRevision)
    : Exception(
        $"Resource ACL of '{resourceName}/{resourceKey}' was modified by someone else " +
        $"(expected revision {expectedRevision}, actual {actualRevision}).")
{
    public string ResourceName { get; } = resourceName;

    public string ResourceKey { get; } = resourceKey;

    public long ExpectedRevision { get; } = expectedRevision;

    /// <summary>存储中的真实版本，取自冲突发生后的重新读取。</summary>
    public long ActualRevision { get; } = actualRevision;
}

/// <summary>
/// 写入了读取端无法识别的 ACL 主体或空标识时抛出。
/// </summary>
/// <remarks>
/// 读取端只认 <see cref="PermissionGrantProviderNames.User"/> 与
/// <see cref="PermissionGrantProviderNames.Role"/>；写进别的 ProviderName、或任一标识为空，
/// 记录会静默失效——既不放行也不拒绝，只是永远匹配不上，成为查不出原因的脏数据。
/// 在唯一写入口拒掉，比让它躺在库里等人发现好。
/// </remarks>
public sealed class InvalidResourceGrantSubjectException(string resourceName, string resourceKey, string reason)
    : Exception($"Resource grant on '{resourceName}/{resourceKey}' is not addressable: {reason}")
{
    public string ResourceName { get; } = resourceName;

    public string ResourceKey { get; } = resourceKey;
}

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
    /// 获取某个资源实例上的全部 ACL 授予与当前版本。
    /// </summary>
    /// <remarks>
    /// 版本必须与授予一起返回：编辑界面保存时要把它回传给
    /// <see cref="IResourceGrantManager.ReplaceGrantsAsync"/>，否则拿不到乐观并发保护。
    /// </remarks>
    Task<ResourceGrantSet> GetGrantsAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定主体在某个资源实例上对每个操作的最终效果（拒绝优先）。
    /// </summary>
    Task<IReadOnlyDictionary<string, ResourceGrantEffect>> GetEffectiveGrantsAsync(
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

    /// <summary>
    /// 构造"指定主体被显式拒绝某操作的资源 Key"查询。
    /// </summary>
    /// <remarks>
    /// <see cref="QueryGrantedResourceKeys"/> 表达的是"ACL 允许减去 ACL 拒绝"，
    /// 它减不掉**经其他途径可见**的资源。一份资源若由数据范围（部门可见）放行、
    /// 又被 ACL 显式拒绝某个人，只用前者组合列表时它依然会出现——而"分享给部门、排除这一个人"
    /// 正是显式拒绝的唯一用途，在列表里失效等于这个能力是假的。
    ///
    /// 完整的可见集合是：<c>(数据范围 OR ACL 允许) AND NOT ACL 拒绝</c>。典型用法：
    /// <code>
    /// var denied = store.QueryDeniedResourceKeys("Orders", ResourceOperations.Read, userId, roleIds);
    /// query = query.Where(order => !denied.Contains(order.ResourceKey));
    /// </code>
    /// </remarks>
    IQueryable<string> QueryDeniedResourceKeys(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds);
}

/// <summary>
/// 写入了未定义的 <see cref="ResourceGrantEffect"/> 时抛出。
/// </summary>
/// <remarks>
/// 枚举在 .NET 里可以承载任意底层值（<c>(ResourceGrantEffect)999</c> 是合法表达式），
/// 判定端又必须 fail-closed，因此非法值一旦落库就会把资源静默变成"任何人都不许访问"。
/// 在唯一的写入口拒掉，比让它先存进去再靠读取端兜底更早、也更好排查。
/// </remarks>
public sealed class InvalidResourceGrantEffectException(
    string resourceName,
    string resourceKey,
    ResourceGrantEffect effect)
    : Exception($"Resource grant effect '{effect}' on '{resourceName}/{resourceKey}' is not a defined value.")
{
    public string ResourceName { get; } = resourceName;

    public string ResourceKey { get; } = resourceKey;

    public ResourceGrantEffect Effect { get; } = effect;
}

/// <summary>
/// 资源 ACL 管理器（写入）。
/// </summary>
public interface IResourceGrantManager
{
    /// <summary>
    /// 原子替换某个资源实例上的全部 ACL，返回写入后的版本。
    /// </summary>
    /// <param name="resourceName">资源类型名。</param>
    /// <param name="resourceKey">资源实例 Key。</param>
    /// <param name="grants">替换后的完整 ACL。</param>
    /// <param name="expectedRevision">
    /// 期望的当前版本，通常来自 <see cref="IResourceGrantStore.GetGrantsAsync"/>。
    /// 与存储中的版本不一致时抛 <see cref="ResourceGrantConcurrencyException"/>。
    /// 传 <c>null</c> 表示不做并发校验，仅用于种子数据这类无并发编辑的场景。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<long> ReplaceGrantsAsync(
        string resourceName,
        string resourceKey,
        IReadOnlyCollection<ResourceGrant> grants,
        long? expectedRevision = null,
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
