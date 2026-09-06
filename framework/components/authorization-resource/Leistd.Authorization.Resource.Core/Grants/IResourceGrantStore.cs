using Leistd.Authorization.Constants;
using Leistd.Authorization.Resource.Exceptions;

namespace Leistd.Authorization.Resource.Grants;

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
/// <param name="Version">ACL 版本，尚未写入过时为 0。保存时回传即可获得乐观并发保护。</param>
public sealed record ResourceGrantSet(
    string ResourceName,
    string ResourceKey,
    IReadOnlyList<ResourceGrant> Grants,
    long Version);

/// <summary>
/// 资源 ACL 存储（只读）。
/// </summary>
/// <remarks>
/// 列表、统计与导出不能先加载候选再逐条判定，否则分页总数、排序和性能都会错误，
/// 因此提供本集合级入口：它返回可被数据库翻译的 <see cref="IQueryable{T}"/>，
/// 调用方以 <c>Contains</c> 合并进业务查询，数据库据此生成 EXISTS/IN 子查询。
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
    /// 返回的查询已排除存在显式拒绝的资源 Key。
    /// <b>必须与调用方的业务查询出自同一个 DbContext 实例</b>，否则 EF 无法把 <c>Contains</c> 翻译进同一条 SQL——本方法因此是异步的。
    /// 业务实体应持有与 ACL 一致的字符串资源 Key 列。
    /// </remarks>
    /// <example>
    /// <code>
    /// var keys = await store.QueryGrantedResourceKeysAsync("Orders", ResourceOperations.Read, userId, roleIds);
    /// query = query.Where(order =&gt; keys.Contains(order.ResourceKey));
    /// </code>
    /// </example>
    Task<IQueryable<string>> QueryGrantedResourceKeysAsync(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构造"指定主体被显式拒绝某操作的资源 Key"查询。
    /// </summary>
    /// <remarks>
    /// <see cref="QueryGrantedResourceKeysAsync"/> 减不掉经其他途径（如数据范围）可见的资源。
    /// 完整可见集合是 <c>(数据范围 OR ACL 允许) AND NOT ACL 拒绝</c>，因此还要减去本方法返回的拒绝集。
    /// 同样要求与业务查询出自同一个 DbContext 实例。
    /// </remarks>
    /// <example>
    /// <code>
    /// var denied = await store.QueryDeniedResourceKeysAsync("Orders", ResourceOperations.Read, userId, roleIds);
    /// query = query.Where(order =&gt; !denied.Contains(order.ResourceKey));
    /// </code>
    /// </example>
    Task<IQueryable<string>> QueryDeniedResourceKeysAsync(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);
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
    /// <param name="expectedVersion">
    /// 期望的当前版本，通常来自 <see cref="IResourceGrantStore.GetGrantsAsync"/>。
    /// 与存储中的版本不一致时抛 <see cref="ResourceGrantConcurrencyException"/>。
    /// 传 <c>null</c> 表示不做并发校验，仅用于种子数据这类无并发编辑的场景。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<long> ReplaceGrantsAsync(
        string resourceName,
        string resourceKey,
        IReadOnlyCollection<ResourceGrant> grants,
        long? expectedVersion = null,
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

    /// <summary>
    /// 清理某个<b>主体</b>在全部资源上的 ACL 条目。用户、角色或客户端被永久删除后调用；重复调用是幂等的。
    /// </summary>
    /// <remarks>
    /// <b>删除主体时必须调用，否则留下永久孤儿。</b>ACL 行以 <c>(ProviderName, ProviderKey)</c> 指向主体，
    /// 通用 ACL 表对身份表建不了外键；不清理的话这些行谁也看不到，而<b>主体标识被重用时它们会重新生效</b>。
    /// 功能权限侧的 <c>IPermissionGrantManager.RemoveProviderAsync</c> 应在同一处删除流程里成对调用。
    /// <b>软删除不应调用本方法</b>——可恢复的主体，其授予也该跟着恢复。
    /// </remarks>
    Task<int> RemoveProviderAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);
}
