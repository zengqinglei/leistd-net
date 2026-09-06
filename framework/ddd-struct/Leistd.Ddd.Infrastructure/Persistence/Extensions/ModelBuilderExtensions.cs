using System.Linq.Expressions;
using Leistd.Auditing;
using Leistd.Ddd.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query;
using Leistd.Auditing.Abstractions;

namespace Leistd.Ddd.Infrastructure.Persistence.Extensions;

/// <summary>
/// 提供领域实体模型约定和全局过滤器扩展。
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// 配置实体基类属性和审计字段长度约束。
    /// </summary>
    public static void ConfigureByConvention<TEntity>(this EntityTypeBuilder<TEntity> b)
        where TEntity : class
    {
        var type = typeof(TEntity);

        if (typeof(ICreationAuditedObject).IsAssignableFrom(type))
        {
            b.Property(nameof(ICreationAuditedObject.CreatorId)).HasMaxLength(64);
        }

        if (typeof(IModificationAuditedObject).IsAssignableFrom(type))
        {
            b.Property(nameof(IModificationAuditedObject.LastModifierId)).HasMaxLength(64);
        }

        if (typeof(IDeletionAuditedObject).IsAssignableFrom(type))
        {
            b.Property(nameof(IDeletionAuditedObject.DeleterId)).HasMaxLength(64);
        }

        if (typeof(IHasConcurrencyStamp).IsAssignableFrom(type))
        {
            // IsRequired 不能省：列可空时 EF 对 null 原值生成 WHERE ConcurrencyStamp IS NULL，
            // 会匹配所有 null 行并使并发校验失效。
            b.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp))
                .HasMaxLength(40)
                .IsRequired()
                .IsConcurrencyToken();
        }
    }


    /// <summary>
    /// 为满足指定契约的所有根实体应用命名全局查询过滤器。
    /// </summary>
    /// <typeparam name="TInterface">接口类型（如 ISoftDelete、IMultiTenant）</typeparam>
    /// <param name="modelBuilder">模型构建器</param>
    /// <param name="filterName">过滤器名称。同一实体上不同名称的过滤器共存（AND 组合），同名后写覆盖先写</param>
    /// <param name="expression">过滤表达式</param>
    /// <remarks>
    /// 软删除与租户隔离各占一个命名查询过滤器，同时命中两个接口的实体两个过滤器叠加生效，
    /// 可按名单独 <c>IgnoreQueryFilters(["名称"])</c> 豁免。
    /// </remarks>
    public static void ApplyGlobalFilters<TInterface>(
        this ModelBuilder modelBuilder,
        string filterName,
        Expression<Func<TInterface, bool>> expression)
    {
        var entities = modelBuilder.Model
            .GetEntityTypes()
            .Where(t => t.BaseType == null)
            .Select(t => t.ClrType)
            .Where(t => typeof(TInterface).IsAssignableFrom(t));

        foreach (var entityType in entities)
        {
            var newParam = Expression.Parameter(entityType);

            var newBody = ReplacingExpressionVisitor.Replace(
                expression.Parameters.Single(),
                newParam,
                expression.Body);

            modelBuilder.Entity(entityType)
                .HasQueryFilter(filterName, Expression.Lambda(newBody, newParam));
        }
    }
}
