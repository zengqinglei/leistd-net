namespace Leistd.Authorization;

/// <summary>
/// 权限授予存储。
/// </summary>
public interface IPermissionGrantStore
{
    /// <summary>
    /// 检查用户是否被授予权限。
    /// </summary>
    Task<bool> IsGrantedToUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查角色是否被授予权限。
    /// </summary>
    Task<bool> IsGrantedToRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查任一角色是否被授予权限。
    /// </summary>
    Task<bool> IsGrantedToAnyRoleAsync(
        string permissionName,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量检查用户或其角色是否被授予权限。
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> IsGrantedToUserOrRolesAsync(
        IReadOnlyCollection<string> permissionNames,
        string userId,
        IReadOnlyCollection<string> roleIds,
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
