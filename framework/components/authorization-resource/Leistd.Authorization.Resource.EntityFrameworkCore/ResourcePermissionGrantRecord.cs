using Leistd.Auditing;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// 资源实例 ACL 持久化实体。
/// </summary>
/// <remarks>
/// <para>与功能权限的 <c>PermissionGrantRecord</c> 分表存储：两者的标识维度不同，
/// 混表会产生大量可空列和含混索引。通用 ACL 表无法对任意业务表建立外键，
/// 因此资源删除后需要显式调用清理，并安排周期性孤儿检查。</para>
/// <para>实现 <see cref="IMultiTenant"/>：ACL 随资源按租户分区，
/// TenantId 由多租户落值拦截器在保存时填充。</para>
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

/// <summary>
/// ResourcePermissionGrantRecord EF Core 实体配置。
/// </summary>
public class ResourcePermissionGrantRecordConfiguration
    : IEntityTypeConfiguration<ResourcePermissionGrantRecord>
{
    public void Configure(EntityTypeBuilder<ResourcePermissionGrantRecord> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResourceName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ResourceKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Operation)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ProviderName)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProviderKey)
            .HasMaxLength(128)
            .IsRequired();

        // 以字符串存储：可读、可演进，且枚举成员重排序不会让既有数据错位。
        builder.Property(x => x.Effect)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // 字符串列挡不住越界值：EF 的枚举转换会把 (ResourceGrantEffect)999 存成 "999"，
        // 读回来还能解析成 999。约束写在数据库上，绕过 Manager 的直连写入也逃不掉。
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ResourcePermissionGrants_Effect",
            $"\"{nameof(ResourcePermissionGrantRecord.Effect)}\" IN ('{nameof(ResourceGrantEffect.Granted)}', '{nameof(ResourceGrantEffect.Prohibited)}')"));

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        // ACL 随资源按租户分区。宿主行与租户行分别用带过滤的唯一索引：
        // 可空 TenantId 直接进唯一索引时 NULL 互不相等，宿主行失去唯一性兜底
        builder.HasIndex(x => new
            {
                x.ResourceName,
                x.ResourceKey,
                x.Operation,
                x.ProviderName,
                x.ProviderKey
            })
            .IsUnique()
            .HasFilter($"\"{nameof(ResourcePermissionGrantRecord.TenantId)}\" IS NULL");

        builder.HasIndex(x => new
            {
                x.TenantId,
                x.ResourceName,
                x.ResourceKey,
                x.Operation,
                x.ProviderName,
                x.ProviderKey
            })
            .IsUnique()
            .HasFilter($"\"{nameof(ResourcePermissionGrantRecord.TenantId)}\" IS NOT NULL");

        // 集合级查询入口：按资源类型 + 操作 + 主体过滤，支撑合并进业务查询的 IN/EXISTS。
        builder.HasIndex(x => new { x.TenantId, x.ResourceName, x.Operation, x.ProviderName, x.ProviderKey });

        // 资源删除后的清理入口。
        builder.HasIndex(x => new { x.ResourceName, x.ResourceKey });
    }
}
