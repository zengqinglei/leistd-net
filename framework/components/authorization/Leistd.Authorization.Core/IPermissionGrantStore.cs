namespace Leistd.Authorization;

/// <summary>
/// 权限授予存储。
/// </summary>
public interface IPermissionGrantStore
{
    /// <summary>
    /// 检查指定提供方是否被授予权限。
    /// </summary>
    Task<bool> IsGrantedAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);
}
