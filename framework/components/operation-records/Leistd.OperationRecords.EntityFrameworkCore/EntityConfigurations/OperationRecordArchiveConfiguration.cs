using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.OperationRecords.EntityFrameworkCore.EntityConfigurations;

/// <summary>
/// <see cref="OperationRecordArchive"/> 的实体配置。
/// </summary>
/// <remarks>
/// 列长度逐一引用 <see cref="OperationRecordInfo"/> 的上限常量，与原表同一个事实源：写字面量的话，
/// 原表某天调整了长度，归档表就会在搬运时静默截断——截断发生在"已经决定长期留存"的那一步上。
/// </remarks>
public class OperationRecordArchiveConfiguration : IEntityTypeConfiguration<OperationRecordArchive>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OperationRecordArchive> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(OperationRecordInfo.MaxActionLength).IsRequired();
        builder.Property(x => x.TargetId).HasMaxLength(OperationRecordInfo.MaxTargetIdLength).IsRequired();
        builder.Property(x => x.AuthorizationBasis).HasMaxLength(OperationRecordInfo.MaxAuthorizationBasisLength).IsRequired();

        // 与原表同样存字符串：归档表是最可能被人直接查的表，序号要对着枚举翻译，且枚举重排后历史含义会静默改变
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        // 给存量表加这一列时，没有库默认值的行会落空串，读取时枚举转换失败——升级后才炸
        builder.Property(x => x.Visibility)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(OperationVisibility.Host)
            // 必须配 ValueGeneratedNever：HasDefaultValue 会让 EF 在属性等于 CLR 默认值时省略该列，
            // 而 OperationVisibility.Tenant 正好是 0——租户可见的记录会被库默认值静默写成宿主可见。
            // 库默认值只为"给存量表加列"的回填服务，不参与 EF 的插入
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.ActorId).HasMaxLength(OperationRecordInfo.MaxActorIdLength);
        builder.Property(x => x.ActorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
        builder.Property(x => x.ImpersonatorId).HasMaxLength(OperationRecordInfo.MaxActorIdLength);
        builder.Property(x => x.ImpersonatorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(OperationRecordInfo.MaxCorrelationIdLength);
        builder.Property(x => x.TargetName).HasMaxLength(OperationRecordInfo.MaxTargetNameLength);
        builder.Property(x => x.FailureCode).HasMaxLength(OperationRecordInfo.MaxFailureCodeLength);
        builder.Property(x => x.FailureDetail).HasMaxLength(OperationRecordInfo.MaxFailureDetailLength);

        // 归档的唯一读法是"某段时间的历史"，按原始发生时间建索引，而不是归档时刻
        builder.HasIndex(x => new { x.TenantId, x.CreationTime }).IsDescending(false, true);
    }
}
