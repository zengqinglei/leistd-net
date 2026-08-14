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

        // 唯一性交给数据库：管理器的"先查后插"挡不住并发——两个请求同时通过校验就会
        // 写入两个同名活跃租户，之后按名称查找的结果不确定。低频与权限门禁都不是不变量。
        // 过滤到未删除行，保留"删除后名称可复用"的语义。
        // PostgreSQL 与 SQLite（3.23+）都支持部分索引与 false 字面量，谓词写法通用
        builder.HasIndex(x => x.NormalizedName)
            .IsUnique()
            .HasFilter($"\"{nameof(TenantRecord.IsDeleted)}\" = false");
    }
}
