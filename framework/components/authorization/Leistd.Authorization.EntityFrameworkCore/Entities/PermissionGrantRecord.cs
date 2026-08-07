using Leistd.Auditing;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 权限授予持久化实体。实现创建审计接口，审计字段由审计拦截器统一填充。
/// </summary>
/// <remarks>
/// 唯一性由 <c>(PermissionName, ProviderName, ProviderKey)</c> 唯一索引保证。
/// <see cref="Effect"/> 是授予的值而非标识，因此不纳入唯一索引——否则同一主体对同一权限
/// 可以同时存在允许与拒绝两行。写入请统一通过 <see cref="IPermissionGrantManager"/>，
/// 它会在写入时补齐祖先并级联清理子孙；直接构造本实体写库会绕过归一化。
/// </remarks>
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

    /// <summary>授予效果：显式允许或显式拒绝。</summary>
    public PermissionGrantEffect Effect { get; set; } = PermissionGrantEffect.Granted;

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }
}
