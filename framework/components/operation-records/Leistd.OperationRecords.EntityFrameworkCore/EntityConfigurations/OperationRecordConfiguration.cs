using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.OperationRecords.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// OperationRecord EF Core 实体配置。
/// </summary>
public class OperationRecordConfiguration : IEntityTypeConfiguration<OperationRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OperationRecord> builder)
    {
        // 表名沿用 EF Core 默认约定，不加框架前缀污染宿主库。
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action)
            .HasMaxLength(OperationRecordInfo.MaxActionLength)
            .IsRequired();

        builder.Property(x => x.TargetId)
            .HasMaxLength(OperationRecordInfo.MaxTargetIdLength)
            .IsRequired();

        builder.Property(x => x.AuthorizationBasis)
            .HasMaxLength(OperationRecordInfo.MaxAuthorizationBasisLength)
            .IsRequired();

        // 存字符串而不是序号：审计表会被人直接查，序号要对着枚举定义翻译；
        // 且枚举重排之后历史行的含义会静默改变。宿主若有全局 Enum 约定，这里的显式配置优先。
        builder.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // 与 Outcome 同一理由存字符串而非序号：审计表会被人直接查，序号要对着枚举定义翻译；
        // 且枚举重排之后历史行的含义会静默改变——而可见性一旦被改错就是越权。
        // 库默认值不是冗余：宿主给存量表加这一列时，没有默认值的历史行会落空串，
        // 读取时枚举转换失败——升级部署后才炸
        builder.Property(x => x.Visibility)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(OperationVisibility.Host)
            // 必须配 ValueGeneratedNever：HasDefaultValue 会让 EF 在属性等于 CLR 默认值时省略该列，
            // 而 OperationVisibility.Tenant 正好是 0——租户可见的记录会被库默认值静默写成宿主可见。
            // 库默认值只为"给存量表加列"的回填服务，不参与 EF 的插入
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.ActorId).HasMaxLength(OperationRecord.MaxActorIdLength);
        builder.Property(x => x.ActorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
        builder.Property(x => x.ImpersonatorId).HasMaxLength(OperationRecord.MaxActorIdLength);
        builder.Property(x => x.ImpersonatorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(OperationRecordInfo.MaxCorrelationIdLength);
        builder.Property(x => x.TargetName).HasMaxLength(OperationRecordInfo.MaxTargetNameLength);
        builder.Property(x => x.FailureCode).HasMaxLength(OperationRecordInfo.MaxFailureCodeLength);
        builder.Property(x => x.FailureDetail).HasMaxLength(OperationRecordInfo.MaxFailureDetailLength);
        // FailureData 刻意不设长度上限：它是 JSON 参数对象，设上限会把一个本该完整的 JSON
        // 截成非法串，读取方拿到"看起来有值但解析必然失败"的数据，比不存更糟。

        // 唯一的查询形态就是"某租户的最近若干条"，因此索引按 (租户, 时间倒序)。
        // 关键字检索走这条索引之后的过滤：审计表按时间裁剪之后剩余集合很小，
        // 为关键字单独建索引只会拖慢写入——而这张表是写多读少的。
        builder.HasIndex(x => new { x.TenantId, x.CreationTime }).IsDescending(false, true);

        // 可见性进索引：每一次查询都带它做过滤（宿主之外的读者读不到 Host 层记录），
        // 不进索引会让这个必然出现的谓词退化成对时间区间结果集的逐行筛。
        builder.HasIndex(x => new { x.TenantId, x.Visibility, x.CreationTime }).IsDescending(false, false, true);

        // 保留期归档的访问路径：整库按时间扫、最旧的先搬（IgnoreQueryFilters，不带 TenantId）。
        // 上面两条都以 TenantId 打头，用不上——不补这一条，归档每一批都要全表扫加排序，
        // 而这张表本就是只涨不消的。升序与归档的 OrderBy 一致，取满一批即可停。
        builder.HasIndex(x => x.CreationTime);
    }
}
