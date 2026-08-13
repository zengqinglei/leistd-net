using Leistd.Auditing;
using Leistd.MultiTenancy;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 权限授予持久化实体。实现创建审计接口，审计字段由审计拦截器统一填充。
/// </summary>
/// <remarks>
/// <para>唯一性由 <c>(TenantId, PermissionName, ProviderName, ProviderKey)</c> 唯一索引保证：
/// 一行即代表一次授予，没有"拒绝"这一维，因此不存在同一主体对同一权限出现两行的可能。</para>
/// <para>实现 <see cref="IMultiTenant"/>：授予按租户分区——全局查询过滤器使 Store 的既有查询
/// 天然只见当前租户的授予，TenantId 由多租户落值拦截器在保存时填充。</para>
/// <para>写入请统一通过 <see cref="IPermissionGrantManager"/>，它会在写入时补齐祖先并级联清理子孙；
/// 直接构造本实体写库会绕过归一化。</para>
/// </remarks>
public class PermissionGrantRecord : ICreationAuditedObject, IMultiTenant
{
    /// <summary>权限授予 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>所属租户 Id，null 为宿主授予。</summary>
    public Guid? TenantId { get; set; }

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
}
