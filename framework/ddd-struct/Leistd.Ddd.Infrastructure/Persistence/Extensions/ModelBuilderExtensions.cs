using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Leistd.Ddd.Infrastructure.Persistence.Extensions;

/// <summary>
/// 提供全局查询过滤器扩展。
/// </summary>
public static class ModelBuilderExtensions
{
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
