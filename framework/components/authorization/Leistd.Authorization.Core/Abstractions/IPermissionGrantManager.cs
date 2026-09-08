using Leistd.Authorization.Constants;
using Leistd.Authorization.Exceptions;

namespace Leistd.Authorization.Abstractions;

/// <summary>
/// 权限授予管理器（写入）。
/// </summary>
/// <remarks>
/// 写入时对权限树做归一化：授予补齐全部祖先，撤销级联清理全部子孙。
/// 授予是纯加法，没有显式拒绝；收回能力靠调整角色构成。取舍见 authorization 组件文档。
/// </remarks>
public interface IPermissionGrantManager
{
    /// <summary>
    /// 清理某个主体的全部授予与授权版本。主体被永久删除后调用；重复调用是幂等的。
    /// </summary>
    /// <remarks>
    /// 与"撤销到空集合"不同：撤销保留并递增版本，本方法连版本行一并删除。
    /// 软删除不应调用——可恢复的主体，其权限也该跟着恢复。
    /// </remarks>
    /// <param name="providerName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除的授予行数。</returns>
    Task<int> RemoveProviderAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 授予单个权限；已授予时不重复写入。
    /// </summary>
    /// <param name="permissionName">权限名称，必须是已定义且启用的权限。</param>
    /// <param name="providerName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task GrantAsync(
        string permissionName,
        string providerName,
        string providerKey,
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
    /// <param name="permissionNames">目标权限名集合，未出现的权限视为撤销。</param>
    /// <param name="expectedVersion">
    /// 期望的当前版本，用于乐观并发。传 <c>null</c> 表示不做并发校验。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>替换后的新版本号。</returns>
    /// <exception cref="PermissionGrantConcurrencyException">
    /// <paramref name="expectedVersion"/> 与存储中的当前版本不一致时抛出。
    /// </exception>
    Task<long> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        IReadOnlyCollection<string> permissionNames,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);
}
