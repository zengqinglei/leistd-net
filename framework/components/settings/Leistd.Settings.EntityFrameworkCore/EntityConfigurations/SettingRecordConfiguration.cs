using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.Settings.EntityFrameworkCore.Entities;

namespace Leistd.Settings.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// SettingRecord EF Core 实体配置。
/// </summary>
public class SettingRecordConfiguration : IEntityTypeConfiguration<SettingRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingRecord> builder)
    {
        // 表名沿用 EF Core 默认约定，不加框架前缀污染宿主库。
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).HasMaxLength(128);

        builder.Property(x => x.ScopeKey)
            .HasMaxLength(192)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Value)
            .HasMaxLength(2000)
            .IsRequired();

        // 用非空 ScopeKey 约束同层级设置唯一性，避免可空 TenantId/UserId 使宿主行失去约束。
        builder.HasIndex(x => new { x.ScopeKey, x.Name }).IsUnique();
    }
}
