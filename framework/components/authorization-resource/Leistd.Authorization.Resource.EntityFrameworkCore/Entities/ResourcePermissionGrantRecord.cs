using Leistd.Auditing;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.Authorization.Resource.Grants;
using Leistd.Auditing.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.Resource.EntityFrameworkCore.Entities;

/// <summary>
/// 资源实例 ACL 持久化实体。
/// </summary>
/// <remarks>
/// 与功能权限的 <c>PermissionGrantRecord</c> 分表存储：两者标识维度不同，混表会产生大量可空列和含混索引。
/// 通用 ACL 表无法对任意业务表建外键，因此资源删除后需显式调用清理，并安排周期性孤儿检查。
/// 实现 <see cref="IMultiTenant"/>：ACL 随资源按租户分区。
/// </remarks>
public class ResourcePermissionGrantRecord : ICreationAuditedObject, IMultiTenant
{
    /// <summary>ACL 记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>所属租户 Id，null 为宿主资源的 ACL。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>资源类型名称。</summary>
    public string ResourceName { get; set; } = default!;

    /// <summary>资源实例 Key。</summary>
    public string ResourceKey { get; set; } = default!;

    /// <summary>被授予的操作。</summary>
    public string Operation { get; set; } = default!;

    /// <summary>授予对象类型，如 User、Role。</summary>
    public string ProviderName { get; set; } = default!;

    /// <summary>授予对象 Key。</summary>
    public string ProviderKey { get; set; } = default!;

    /// <summary>授予效果：显式允许或显式拒绝。</summary>
    public ResourceGrantEffect Effect { get; set; } = ResourceGrantEffect.Granted;

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }
}
