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
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(32).IsRequired();

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
