namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 在宿主库与每个独立库里各执行一次同一段逻辑，逐库隔离失败。
/// </summary>
/// <remarks>
/// <para>封装"取物理库清单 → 切到代表租户的上下文 → 执行 → 记录失败、继续下一个库"。回调运行时租户上下文已切好，
/// 需要事务时在回调里 <c>BeginAsync(requiresNew: true)</c>、经 <c>IDbContextProvider</c> 取上下文，顺序自然正确；
/// 执行器不替回调开工作单元，批量任务可以一批一个事务。</para>
/// <para>一个库失败只记日志并计入结果，其余库照常执行；调用方取消时停止并抛出。</para>
/// </remarks>
/// <example>
/// <code>
/// var result = await runner.ForEachDatabaseAsync(ConnectionStringNames.Default, async (database, ct) =&gt;
/// {
///     using var uow = await unitOfWorkManager.BeginAsync(requiresNew: true);
///     var dbContext = await dbContextProvider.GetDbContextAsync(ct);
///     // 同库其余租户的数据由 IgnoreQueryFilters() 一并覆盖
///     await uow.CompleteAsync(ct);
/// }, cancellationToken);
/// </code>
/// </example>
public interface ITenantDatabaseRunner
{
    /// <summary>对指定连接名下的每个物理库执行 <paramref name="action"/>。</summary>
    /// <param name="connectionStringName">连接名，通常是业务 DbContext 的 <c>[ConnectionStringName]</c>。</param>
    /// <param name="action">在某个库的租户上下文里执行的逻辑。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处理的库数与失败的库。</returns>
    Task<TenantDatabaseRunResult> ForEachDatabaseAsync(
        string connectionStringName,
        Func<TenantDatabase, CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一次逐库执行的结果。
/// </summary>
/// <param name="Databases">处理的物理库数（含宿主库）。</param>
/// <param name="FailedDatabases">执行失败的库；错误已记日志。</param>
public sealed record TenantDatabaseRunResult(int Databases, IReadOnlyList<TenantDatabase> FailedDatabases);
