namespace Leistd.Authorization;

/// <summary>
/// 权限授予管理器。
/// </summary>
public interface IPermissionGrantManager
{
    /// <summary>
    /// 将权限授予用户。
    /// </summary>
    Task GrantToUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将权限授予角色。
    /// </summary>
    Task GrantToRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销用户权限。
    /// </summary>
    Task RevokeFromUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销角色权限。
    /// </summary>
    Task RevokeFromRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户已授予的权限。
    /// </summary>
    Task<IReadOnlySet<string>> GetGrantedPermissionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取角色已授予的权限。
    /// </summary>
    Task<IReadOnlySet<string>> GetGrantedPermissionsForRoleAsync(
        string roleId,
        CancellationToken cancellationToken = default);
}
