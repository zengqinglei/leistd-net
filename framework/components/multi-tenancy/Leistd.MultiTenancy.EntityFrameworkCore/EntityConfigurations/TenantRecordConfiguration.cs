using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// TenantRecord EF Core 实体配置
/// </summary>
public class TenantRecordConfiguration : IEntityTypeConfiguration<TenantRecord>
{
    public void Configure(EntityTypeBuilder<TenantRecord> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.NormalizedName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(128);

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        builder.Property(x => x.LastModifierId)
            .HasMaxLength(64);

        builder.Property(x => x.DeleterId)
            .HasMaxLength(64);

        // 非唯一索引：唯一性由 ITenantManager 在未删除行中校验——
        // 软删除行仍占据名称时，带过滤的唯一索引在各 Provider 上语法不一，
        // 而租户创建是低频、有权限门禁的管理操作，管理器级校验足够
        builder.HasIndex(x => x.NormalizedName);
    }
}
