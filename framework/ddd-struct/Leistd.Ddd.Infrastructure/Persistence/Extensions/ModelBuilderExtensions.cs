using System.Linq.Expressions;
using Leistd.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query;

namespace Leistd.Ddd.Infrastructure.Persistence.Extensions;

/// <summary>
/// ModelBuilder 扩展方法
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// 配置实体基类属性和审计字段长度约束。
    /// 应在每个实体的 Fluent API 配置块中优先调用
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
    }


    /// <summary>
    /// 为所有实现指定接口的实体类型应用<b>命名</b>全局查询过滤器
    /// </summary>
    /// <typeparam name="TInterface">接口类型（如 ISoftDelete、IMultiTenant）</typeparam>
    /// <param name="modelBuilder">模型构建器</param>
    /// <param name="filterName">过滤器名称。同一实体上不同名称的过滤器共存（AND 组合），同名后写覆盖先写</param>
    /// <param name="expression">过滤表达式</param>
    /// <remarks>
    /// 使用 EF 10 命名查询过滤器：软删除与租户隔离各占一个名字，
    /// 同时命中两个接口的实体两个过滤器叠加生效，且可按名单独
    /// <c>IgnoreQueryFilters(["名称"])</c> 豁免。
    /// 使用 ReplacingExpressionVisitor 做参数替换（无反射调用开销）。
    ///
    /// 参考：
    /// - https://learn.microsoft.com/en-us/ef/core/querying/filters
    /// </remarks>
    public static void ApplyGlobalFilters<TInterface>(
        this ModelBuilder modelBuilder,
        string filterName,
        Expression<Func<TInterface, bool>> expression)
    {
        // 获取所有实现指定接口的根实体类型（排除继承的实体）
        var entities = modelBuilder.Model
            .GetEntityTypes()
            .Where(t => t.BaseType == null) // 只处理根实体类型
            .Select(t => t.ClrType)
            .Where(t => typeof(TInterface).IsAssignableFrom(t));

        foreach (var entityType in entities)
        {
            // 创建新的参数表达式（实体类型）
            var newParam = Expression.Parameter(entityType);

            // 使用 ReplacingExpressionVisitor 替换表达式参数：
            // 将 Expression<Func<TInterface, bool>> 转换为 Expression<Func<TEntity, bool>>
            var newBody = ReplacingExpressionVisitor.Replace(
                expression.Parameters.Single(),
                newParam,
                expression.Body);

            // 应用命名过滤器（EF 10）：不同名称在同一实体上 AND 叠加
            modelBuilder.Entity(entityType)
                .HasQueryFilter(filterName, Expression.Lambda(newBody, newParam));
        }
    }
}
