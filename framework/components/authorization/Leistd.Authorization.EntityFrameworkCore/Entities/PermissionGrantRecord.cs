using Leistd.Auditing;
using Leistd.MultiTenancy;
using Leistd.Auditing.Abstractions;
using Leistd.Authorization.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.EntityFrameworkCore.Entities;

/// <summary>
/// 表示持久化的权限授予记录。
/// </summary>
/// <remarks>
/// 一行即代表一次授予，没有"拒绝"这一维，同一主体对同一权限不会出现两行。
/// 实现 <see cref="IMultiTenant"/>：授予按租户分区，<c>TenantId</c> 由 <c>BaseDbContext</c> 在实体<b>进入变更跟踪时</b>落定。
/// 写入统一通过 <see cref="IPermissionGrantManager"/>；直接构造本实体写库会绕过归一化。
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
