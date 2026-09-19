using CompanyName.ProjectName.Infrastructure.OperationRecords;
using Leistd.OperationRecords.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// 操作记录归档表的实体配置。
/// </summary>
/// <remarks>
/// 列长度逐一引用 <c>OperationRecordInfo.Max*Length</c>，与原表<b>同一个事实源</b>。
/// 写字面量的话，框架某天调整了长度，归档表就会在搬运时静默截断——
/// 而截断发生在"已经决定要长期留存"的那一步上。
/// </remarks>
internal static class OperationRecordArchiveEntityConfiguration
{
    internal static void ConfigureOperationRecordArchives(this ModelBuilder builder)
    {
        builder.Entity<OperationRecordArchive>(b =>
        {
            b.HasKey(x => x.Id);

            b.Property(x => x.Action)
                .HasMaxLength(OperationRecordInfo.MaxActionLength)
                .IsRequired();

            b.Property(x => x.TargetId)
                .HasMaxLength(OperationRecordInfo.MaxTargetIdLength)
                .IsRequired();

            b.Property(x => x.AuthorizationBasis)
                .HasMaxLength(OperationRecordInfo.MaxAuthorizationBasisLength)
                .IsRequired();

            // 与原表同样存字符串而非序号：归档表是最可能被人直接查的表，
            // 序号要对着枚举定义翻译，且枚举重排之后历史行的含义会静默改变。
            b.Property(x => x.Outcome)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            b.Property(x => x.Visibility)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            b.Property(x => x.ActorId).HasMaxLength(OperationRecordInfo.MaxActorIdLength);
            b.Property(x => x.ActorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
            b.Property(x => x.ImpersonatorId).HasMaxLength(OperationRecordInfo.MaxActorIdLength);
            b.Property(x => x.ImpersonatorName).HasMaxLength(OperationRecordInfo.MaxActorNameLength);
            b.Property(x => x.CorrelationId).HasMaxLength(OperationRecordInfo.MaxCorrelationIdLength);
            b.Property(x => x.TargetName).HasMaxLength(OperationRecordInfo.MaxTargetNameLength);
            b.Property(x => x.FailureCode).HasMaxLength(OperationRecordInfo.MaxFailureCodeLength);
            b.Property(x => x.FailureDetail).HasMaxLength(OperationRecordInfo.MaxFailureDetailLength);
            // FailureData 与原表一致，刻意不设上限：它是 JSON，截断会得到无法解析的串。

            // 归档表的唯一读法是"某段时间的历史"，因此按原始发生时间建索引。
            // 不按 ArchivedTime：人要找的是"那件事什么时候发生的"，不是"它哪天被搬走的"。
            b.HasIndex(x => new { x.TenantId, x.CreationTime }).IsDescending(false, true);
        });
    }
}
