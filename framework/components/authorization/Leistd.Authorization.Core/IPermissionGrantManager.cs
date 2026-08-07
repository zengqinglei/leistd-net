namespace Leistd.Authorization;

/// <summary>
/// 权限授予管理器（写入）。
/// </summary>
/// <remarks>
/// 实现须在写入时对权限树做归一化：授予子权限时补齐其全部祖先，撤销父权限时级联清理其全部
/// 子孙。归一化下沉到管理器而非上层 API，使单条授予、批量替换和种子数据走同一条路径，
/// 运行时的权限检查因此可以保持扁平查找，不需要回溯定义树。
/// </remarks>
public interface IPermissionGrantManager
{
    /// <summary>
    /// 授予单个权限；已存在同效果的授予时不重复写入，效果不同则就地更新。
    /// </summary>
    /// <param name="permissionName">权限名称，必须是已定义且启用的权限。</param>
    /// <param name="providerName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="effect">授予效果，默认为允许。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task GrantAsync(
        string permissionName,
        string providerName,
        string providerKey,
        PermissionGrantEffect effect = PermissionGrantEffect.Granted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销单个权限，并级联撤销其全部子孙权限。
    /// </summary>
    Task RevokeAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子替换某个主体的全部授予。
    /// </summary>
    /// <param name="providerName">授予对象类型。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="grants">目标授予集合，未出现的权限视为撤销。</param>
    /// <param name="expectedRevision">
    /// 期望的当前版本，用于乐观并发。传 <c>null</c> 表示不做并发校验。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>替换后的新版本号。</returns>
    /// <exception cref="PermissionGrantConcurrencyException">
    /// <paramref name="expectedRevision"/> 与存储中的当前版本不一致时抛出。
    /// </exception>
    Task<long> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        IReadOnlyCollection<PermissionGrant> grants,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default);
}
