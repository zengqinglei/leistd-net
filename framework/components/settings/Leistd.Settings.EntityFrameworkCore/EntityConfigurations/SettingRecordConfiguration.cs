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

        // 同一层级下同名设置只能有一行，并且是按层级批量读取的覆盖索引。
        // 键用非空的 ScopeKey 而不是 (TenantId, UserId)：那两列可为 NULL，
        // 多个 NULL 互不相等会让宿主级与租户级默认值不受任何约束（见 SettingRecord.ScopeKey）。
        builder.HasIndex(x => new { x.ScopeKey, x.Name }).IsUnique();
    }
}
