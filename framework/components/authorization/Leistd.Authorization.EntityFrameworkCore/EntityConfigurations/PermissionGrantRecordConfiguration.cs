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
