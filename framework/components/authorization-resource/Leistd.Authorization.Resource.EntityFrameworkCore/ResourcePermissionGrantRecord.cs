using Leistd.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// 资源实例 ACL 持久化实体。
/// </summary>
/// <remarks>
/// 与功能权限的 <c>PermissionGrantRecord</c> 分表存储：两者的标识维度不同，
/// 混表会产生大量可空列和含混索引。通用 ACL 表无法对任意业务表建立外键，
/// 因此资源删除后需要显式调用清理，并安排周期性孤儿检查。
/// </remarks>
public class ResourcePermissionGrantRecord : ICreationAuditedObject
{
    /// <summary>ACL 记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

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
    public PermissionGrantEffect Effect { get; set; } = PermissionGrantEffect.Granted;

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

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        builder.HasIndex(x => new
            {
                x.ResourceName,
                x.ResourceKey,
                x.Operation,
                x.ProviderName,
                x.ProviderKey
            })
            .IsUnique();

        // 集合级查询入口：按资源类型 + 操作 + 主体过滤，支撑合并进业务查询的 IN/EXISTS。
        builder.HasIndex(x => new { x.ResourceName, x.Operation, x.ProviderName, x.ProviderKey });

        // 资源删除后的清理入口。
        builder.HasIndex(x => new { x.ResourceName, x.ResourceKey });
    }
}
