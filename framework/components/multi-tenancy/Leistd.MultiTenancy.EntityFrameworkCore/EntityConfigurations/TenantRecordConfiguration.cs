using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;

namespace Leistd.MultiTenancy.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// 配置租户记录的 EF Core 映射。
/// </summary>
public class TenantRecordConfiguration : IEntityTypeConfiguration<TenantRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantRecord> builder)
    {
        builder.HasKey(x => x.Id);

        // 并发令牌：租户生命周期的所有写路径（启停、改名、连接配置创建与修改）
        // 都要竞争它，把"改路由前必须已停用"从一句注释变成数据库层面的不变量
        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.Name)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.NormalizedName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(128);

        builder.Property(x => x.Description)
            .HasMaxLength(256);

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        builder.Property(x => x.LastModifierId)
            .HasMaxLength(64);

        builder.Property(x => x.DeleterId)
            .HasMaxLength(64);

        // 部分唯一索引保证并发下活跃租户名称唯一，同时允许删除后复用名称。
        builder.HasIndex(x => x.NormalizedName)
            .IsUnique()
            .HasFilter($"\"{nameof(TenantRecord.IsDeleted)}\" = false");
    }
}
