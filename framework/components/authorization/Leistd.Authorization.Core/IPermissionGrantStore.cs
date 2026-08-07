namespace Leistd.Authorization;

/// <summary>
/// 权限授予存储（只读）。
/// </summary>
/// <remarks>
/// 只提供"按主体一次取回全部授予"的读取形状：多权限判断、三态组合与有效权限计算
/// 都在内存完成，因此不存在按权限逐条查询导致的 N+1。写入职责属于
/// <see cref="IPermissionGrantManager"/>。
/// </remarks>
public interface IPermissionGrantStore
{
    /// <summary>
    /// 获取单个授予主体（用户或角色）的全部授予及其并发版本。
    /// </summary>
    /// <param name="providerName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该主体的授予集合；没有任何授予时返回空集合且版本为 0。</returns>
    Task<PermissionGrantSet> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次取回同类型多个主体的授予及其并发版本，用于管理界面的列表页。
    /// </summary>
    /// <remarks>
    /// 列表页需要每个主体的授予数量，逐行调用 <see cref="GetGrantsAsync(string, string, CancellationToken)"/>
    /// 会退化成按行的 N+1；此方法的往返次数与主体数量无关。
    /// </remarks>
    /// <param name="providerName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKeys">授予对象 Key 集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与 <paramref name="providerKeys"/> 一一对应的授予集合；无授予的主体返回空集合。</returns>
    Task<IReadOnlyList<PermissionGrantSet>> GetGrantsAsync(
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次取回权限检查主体的全部授予：用户直授加其所属角色的授予。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="roleIds">用户所属角色 ID 集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);
}
