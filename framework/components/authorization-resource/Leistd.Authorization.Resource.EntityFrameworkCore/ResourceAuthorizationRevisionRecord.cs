using Leistd.Auditing;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// 资源实例的 ACL 版本，每次写入递增。
/// </summary>
/// <remarks>
/// 唯一索引只能防止重复行，防不住"两个人基于同一份旧快照各自保存"。
/// 资源 ACL 是全量替换语义，丢失更新的后果特别难受：被覆盖掉的往往正是显式拒绝，
/// 那个本该被排除在外的人会重新经由角色拿到访问权，且两次保存都会显示成功。
/// 功能权限那一层已经用版本号 + 409 收口，资源这一层没有理由用更松的口径。
/// </remarks>
public class ResourceAuthorizationRevisionRecord : IModificationAuditedObject, IMultiTenant
{
    /// <summary>版本记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>所属租户 Id，null 为宿主。版本随 ACL 按租户分区。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>资源类型名。</summary>
    public string ResourceName { get; set; } = default!;

    /// <summary>资源实例 Key。</summary>
    public string ResourceKey { get; set; } = default!;

    /// <summary>当前版本号，从 1 开始，每次 ACL 写入递增。</summary>
    public long Version { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public string? LastModifierId { get; set; }
}

/// <summary>
/// ResourceAuthorizationRevisionRecord EF Core 实体配置。
/// </summary>
public class ResourceAuthorizationRevisionRecordConfiguration
    : IEntityTypeConfiguration<ResourceAuthorizationRevisionRecord>
{
    public void Configure(EntityTypeBuilder<ResourceAuthorizationRevisionRecord> builder)
    {
        builder.ToTable("ResourceAuthorizationRevisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResourceName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ResourceKey)
            .HasMaxLength(128)
            .IsRequired();

        // 并发令牌：EF 会在 UPDATE 上带 WHERE Version = @original，
        // 使「读版本→比较→写入」这段窗口由数据库收口，而不是靠应用层的先读后比。
        builder.Property(x => x.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(x => x.LastModifierId)
            .HasMaxLength(64);

        // 版本随 ACL 按租户分区；宿主行与租户行分别用带过滤的唯一索引（NULL 唯一性说明同 ACL 表）
        builder.HasIndex(x => new { x.ResourceName, x.ResourceKey })
            .IsUnique()
            .HasFilter($"\"{nameof(ResourceAuthorizationRevisionRecord.TenantId)}\" IS NULL");

        builder.HasIndex(x => new { x.TenantId, x.ResourceName, x.ResourceKey })
            .IsUnique()
            .HasFilter($"\"{nameof(ResourceAuthorizationRevisionRecord.TenantId)}\" IS NOT NULL");
    }
}
