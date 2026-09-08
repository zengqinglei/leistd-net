using Leistd.Auditing;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.Authorization.Resource.EntityFrameworkCore.Entities;

namespace Leistd.Authorization.Resource.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// ResourceAuthorizationVersionRecord EF Core 实体配置。
/// </summary>
public class ResourceAuthorizationVersionRecordConfiguration
    : IEntityTypeConfiguration<ResourceAuthorizationVersionRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResourceAuthorizationVersionRecord> builder)
    {
        builder.ToTable("ResourceAuthorizationVersions");

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
            .HasFilter($"\"{nameof(ResourceAuthorizationVersionRecord.TenantId)}\" IS NULL");

        builder.HasIndex(x => new { x.TenantId, x.ResourceName, x.ResourceKey })
            .IsUnique()
            .HasFilter($"\"{nameof(ResourceAuthorizationVersionRecord.TenantId)}\" IS NOT NULL");
    }
}
