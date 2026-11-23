using Leistd.Settings.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Settings.EntityFrameworkCore.EntityConfigurations;

/// <summary>设置测试结果的表映射。</summary>
public class SettingCheckRecordConfiguration : IEntityTypeConfiguration<SettingCheckRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingCheckRecord> builder)
    {
        // 表名沿用 EF Core 默认约定，不加框架前缀
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ScopeKey).HasMaxLength(192).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.Property(x => x.CheckerId).HasMaxLength(64);
        builder.Property(x => x.CheckerName).HasMaxLength(256);

        builder.HasIndex(x => new { x.ScopeKey, x.Name }).IsUnique();
    }
}
