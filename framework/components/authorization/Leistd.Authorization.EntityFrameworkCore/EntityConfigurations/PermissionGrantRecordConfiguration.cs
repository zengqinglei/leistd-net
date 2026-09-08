using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.Authorization.EntityFrameworkCore.Entities;

namespace Leistd.Authorization.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// PermissionGrantRecord EF Core 实体配置。
/// </summary>
public class PermissionGrantRecordConfiguration : IEntityTypeConfiguration<PermissionGrantRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionGrantRecord> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PermissionName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ProviderName)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProviderKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        // 授予按租户分区。可空 TenantId 直接进唯一索引时，PostgreSQL/Sqlite 视 NULL 互不相等，
        // 宿主行会失去唯一性兜底（重复授予与并发首写都拦不住），因此宿主行与租户行
        // 分别用带过滤的唯一索引收口（"列名" 双引号引用在 PostgreSQL 与 Sqlite 上语义一致）
        builder.HasIndex(x => new { x.PermissionName, x.ProviderName, x.ProviderKey })
            .IsUnique()
            .HasFilter($"\"{nameof(PermissionGrantRecord.TenantId)}\" IS NULL");

        builder.HasIndex(x => new { x.TenantId, x.PermissionName, x.ProviderName, x.ProviderKey })
            .IsUnique()
            .HasFilter($"\"{nameof(PermissionGrantRecord.TenantId)}\" IS NOT NULL");

        builder.HasIndex(x => new { x.TenantId, x.ProviderName, x.ProviderKey });
    }
}

/// <summary>
/// AuthorizationVersionRecord EF Core 实体配置。
/// </summary>
public class AuthorizationVersionRecordConfiguration : IEntityTypeConfiguration<AuthorizationVersionRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuthorizationVersionRecord> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderName)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProviderKey)
            .HasMaxLength(128)
            .IsRequired();

        // 并发令牌：EF 会在 UPDATE 上带 WHERE Version = @original，
        // 使「读版本→比较→写入」这段窗口由数据库收口，而不是靠应用层的先读后比。
        builder.Property(x => x.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(x => x.LastModifierId)
            .HasMaxLength(64);

        // 版本随授予按租户分区；宿主行与租户行分别用带过滤的唯一索引（NULL 唯一性说明同上）。
        // 并发首写的兜底依赖这两个索引：两个事务同时为同一主体插入版本行时，落败方由数据库拒绝
        builder.HasIndex(x => new { x.ProviderName, x.ProviderKey })
            .IsUnique()
            .HasFilter($"\"{nameof(AuthorizationVersionRecord.TenantId)}\" IS NULL");

        builder.HasIndex(x => new { x.TenantId, x.ProviderName, x.ProviderKey })
            .IsUnique()
            .HasFilter($"\"{nameof(AuthorizationVersionRecord.TenantId)}\" IS NOT NULL");
    }
}
