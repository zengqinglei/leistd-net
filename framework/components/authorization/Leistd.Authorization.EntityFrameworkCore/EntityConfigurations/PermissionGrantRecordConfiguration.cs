using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// PermissionGrantRecord EF Core 实体配置。
/// </summary>
public class PermissionGrantRecordConfiguration : IEntityTypeConfiguration<PermissionGrantRecord>
{
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

        builder.HasIndex(x => new { x.PermissionName, x.ProviderName, x.ProviderKey })
            .IsUnique();

        builder.HasIndex(x => new { x.ProviderName, x.ProviderKey });
    }
}

/// <summary>
/// AuthorizationRevisionRecord EF Core 实体配置。
/// </summary>
public class AuthorizationRevisionRecordConfiguration : IEntityTypeConfiguration<AuthorizationRevisionRecord>
{
    public void Configure(EntityTypeBuilder<AuthorizationRevisionRecord> builder)
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

        builder.HasIndex(x => new { x.ProviderName, x.ProviderKey })
            .IsUnique();
    }
}
