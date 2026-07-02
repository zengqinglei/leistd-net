using Leistd.Authorization;
using Leistd.Auditing;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 权限授予持久化实体。实现创建审计接口，审计字段由审计拦截器统一填充。
/// </summary>
public class PermissionGrantRecord : ICreationAuditedObject
{
    /// <summary>权限授予 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>权限名称。</summary>
    public string PermissionName { get; set; } = default!;

    /// <summary>授予对象类型，如 User、Role。</summary>
    public string ProviderName { get; set; } = default!;

    /// <summary>授予对象 Key，如 UserId、RoleId。</summary>
    public string ProviderKey { get; set; } = default!;

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }

    /// <summary>
    /// 创建角色权限授予记录。
    /// </summary>
    public static PermissionGrantRecord ForRole(string permissionName, Guid roleId)
        => new()
        {
            PermissionName = permissionName,
            ProviderName = PermissionGrantProviderNames.Role,
            ProviderKey = roleId.ToString()
        };

    /// <summary>
    /// 创建用户权限授予记录。
    /// </summary>
    public static PermissionGrantRecord ForUser(string permissionName, Guid userId)
        => new()
        {
            PermissionName = permissionName,
            ProviderName = PermissionGrantProviderNames.User,
            ProviderKey = userId.ToString()
        };
}

