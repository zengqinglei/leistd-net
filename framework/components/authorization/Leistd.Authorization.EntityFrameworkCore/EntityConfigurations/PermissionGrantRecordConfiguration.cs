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

        // 以字符串存储：可读、可演进，且枚举成员重排序不会让既有数据错位。
        builder.Property(x => x.Effect)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        // Effect 是授予的值而非标识，不纳入唯一索引：否则同一主体对同一权限
        // 可以同时存在允许与拒绝两行。
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

        builder.Property(x => x.Version)
            .IsRequired();

        builder.Property(x => x.LastModifierId)
            .HasMaxLength(64);

        builder.HasIndex(x => new { x.ProviderName, x.ProviderKey })
            .IsUnique();
    }
}
