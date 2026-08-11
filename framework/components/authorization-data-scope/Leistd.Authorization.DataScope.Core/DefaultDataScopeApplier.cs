using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Authorization.DataScope;

/// <summary>
/// 默认数据范围应用器。
/// </summary>
/// <remarks>
/// 组合规则：被分配的多个范围之间取并集（OR）。任一范围返回 <c>null</c>（无限制）时，
/// 整体即为无限制。没有任何分配时按默认拒绝处理，返回空结果集，而不是放行全部数据。
/// 租户隔离、软删除等硬边界不由本组件负责，它们通过 EF Core 全局查询过滤器始终生效，
/// 因此永远与业务范围做 AND，不会被这里的并集放宽。
/// </remarks>
public class DefaultDataScopeApplier(
    IPermissionSubjectProvider subjectProvider,
    IServiceProvider serviceProvider,
    IDataScopeAssignmentProvider assignmentProvider) : IDataScopeApplier
{
    public async ValueTask<IQueryable<TEntity>> ApplyAsync<TEntity>(
        IQueryable<TEntity> query,
        string resourceName,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
            return query.Where(_ => false);

        if (subject.IsSuperAdmin)
            return query;

        var assignments = await assignmentProvider.GetAssignmentsAsync(
            subject,
            resourceName,
            operation,
            cancellationToken);

        // 再按当前资源与操作过滤一遍：分配来源由业务项目实现，查询漏写条件时会返回别的资源
        // 或别的操作的分配，直接采信就等于让"能看订单"顺带放开"能改客户"。
        assignments = [.. assignments.Where(x =>
            string.Equals(x.ResourceName, resourceName, StringComparison.Ordinal) &&
            string.Equals(x.Operation, operation, StringComparison.Ordinal))];

        if (assignments.Count == 0)
            return query.Where(_ => false);

        var context = new DataScopeContext(subject, resourceName, operation, assignments);

        var providers = serviceProvider
            .GetServices<IDataScopeProvider<TEntity>>()
            .Where(x => x.ResourceName == resourceName)
            .ToDictionary(x => x.ScopeName, StringComparer.Ordinal);

        Expression<Func<TEntity, bool>>? combined = null;

        foreach (var assignment in assignments)
        {
            if (!providers.TryGetValue(assignment.ScopeName, out var provider))
            {
                // 分配了一个没有对应 Provider 的范围：无法翻译成谓词，按默认拒绝跳过。
                continue;
            }

            // 不贡献可见性的范围由 Provider 返回 _ => false 表达，并入并集后自然是空贡献；
            // "全部可见"必须显式返回 _ => true。签名不可空，就没有第三种解释的余地。
            var predicate = await provider.BuildPredicateAsync(context, cancellationToken);

            combined = combined == null ? predicate : Or(combined, predicate);
        }

        return combined == null ? query.Where(_ => false) : query.Where(combined);
    }

    private static Expression<Func<TEntity, bool>> Or<TEntity>(
        Expression<Func<TEntity, bool>> left,
        Expression<Func<TEntity, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var body = Expression.OrElse(
            new ParameterRebinder(left.Parameters[0], parameter).Visit(left.Body),
            new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body));

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    /// <summary>
    /// 把两个独立 Lambda 的参数统一到同一个参数上，否则合并后的表达式无法被翻译。
    /// </summary>
    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
