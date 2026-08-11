using Leistd.Auditing;
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
public class ResourceAuthorizationRevisionRecord : IModificationAuditedObject
{
    /// <summary>版本记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

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

        builder.HasIndex(x => new { x.ResourceName, x.ResourceKey })
            .IsUnique();
    }
}
