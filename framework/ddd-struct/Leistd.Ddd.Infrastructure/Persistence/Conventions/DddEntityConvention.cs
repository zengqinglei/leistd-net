using Leistd.Auditing.Abstractions;
using Leistd.Ddd.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Leistd.Ddd.Infrastructure.Persistence.Conventions;

/// <summary>
/// 按实体实现的契约配置审计字段与并发标记列的 EF Core 模型约定。
/// </summary>
/// <remarks>
/// <para>审计人字段（<c>CreatorId</c>、<c>LastModifierId</c>、<c>DeleterId</c>）限长 64；
/// <see cref="IHasConcurrencyStamp.ConcurrencyStamp"/> 限长 40、必填并作为并发令牌。
/// 必填使每一行都带标记参与并发比较；插入前漏写的标记由 <c>ConcurrencyStampSaveChangesInterceptor</c> 补种。</para>
/// <para>以约定来源写入，实体上的显式 Fluent 配置或数据注解优先。
/// 派生自 <see cref="BaseDbContext"/> 的上下文已自动注册；其他上下文在 <c>ConfigureConventions</c> 中
/// 用 <c>configurationBuilder.Conventions.Add(_ =&gt; new DddEntityConvention())</c> 注册。</para>
/// </remarks>
public sealed class DddEntityConvention : IModelFinalizingConvention
{
    private const int AuditUserIdMaxLength = 64;
    private const int ConcurrencyStampMaxLength = 40;

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ICreationAuditedObject).IsAssignableFrom(clrType))
            {
                entityType.FindProperty(nameof(ICreationAuditedObject.CreatorId))
                    ?.Builder.HasMaxLength(AuditUserIdMaxLength);
            }

            if (typeof(IModificationAuditedObject).IsAssignableFrom(clrType))
            {
                entityType.FindProperty(nameof(IModificationAuditedObject.LastModifierId))
                    ?.Builder.HasMaxLength(AuditUserIdMaxLength);
            }

            if (typeof(IDeletionAuditedObject).IsAssignableFrom(clrType))
            {
                entityType.FindProperty(nameof(IDeletionAuditedObject.DeleterId))
                    ?.Builder.HasMaxLength(AuditUserIdMaxLength);
            }

            if (typeof(IHasConcurrencyStamp).IsAssignableFrom(clrType)
                && entityType.FindProperty(nameof(IHasConcurrencyStamp.ConcurrencyStamp)) is { } stamp)
            {
                // 三项各自以约定来源写入：某一项被显式配置覆盖时，其余两项照常生效
                stamp.Builder.HasMaxLength(ConcurrencyStampMaxLength);
                stamp.Builder.IsRequired(true);
                stamp.Builder.IsConcurrencyToken(true);
            }
        }
    }
}
