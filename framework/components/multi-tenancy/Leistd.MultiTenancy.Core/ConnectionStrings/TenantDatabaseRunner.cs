using Leistd.MultiTenancy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy.ConnectionStrings;

internal sealed class TenantDatabaseRunner(
    ITenantDatabaseEnumerator databaseEnumerator,
    ICurrentTenant currentTenant,
    ILogger<TenantDatabaseRunner> logger) : ITenantDatabaseRunner
{
    public async Task<TenantDatabaseRunResult> ForEachDatabaseAsync(
        string connectionStringName,
        Func<TenantDatabase, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var databases = await databaseEnumerator.GetDatabasesAsync(connectionStringName, cancellationToken);
        var failed = new List<TenantDatabase>();

        foreach (var database in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (currentTenant.Change(database.TenantId))
                {
                    await action(database, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 一个库连不上不该让其余的库也跳过；失败的库计入结果，由调用方决定如何报告
                failed.Add(database);
                logger.LogError(exception, "Per-database work failed for {Database}.", database);
            }
        }

        return new TenantDatabaseRunResult(databases.Count, failed);
    }
}
