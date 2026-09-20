using System.Reflection;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Notifications.EntityFrameworkCore.Entities;
using Leistd.Notifications.EntityFrameworkCore.Options;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Notifications.EntityFrameworkCore.Retention;

// 到期通知按物理库逐个清理：IgnoreQueryFilters 只能放开同一个库里的租户，独立库租户的通知在它自己的库里。
// 每批一个工作单元，事务与内存有界；有库失败时抛出，调度器不记水位，该库最迟在下一个调度时段重做（按截止时间扫描，积压会一并清掉）。
internal sealed class NotificationRetentionJob<TDbContext>(
    IOptionsMonitor<NotificationRetentionOptions> retention,
    ITenantDatabaseRunner databaseRunner,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    IClock clock,
    ILogger<NotificationRetentionJob<TDbContext>> logger) : IRecurringJob
    where TDbContext : DbContext
{
    internal const string Name = "notifications.retention";

    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        var current = retention.CurrentValue;
        if (!current.Enabled)
        {
            logger.LogDebug("Notification retention is disabled; skipping this run.");
            return;
        }

        var now = clock.Now;
        var readCutoff = now.AddDays(-current.ReadRetentionDays);
        var unreadCutoff = now.AddDays(-current.UnreadRetentionDays);
        var deleted = 0;

        // 停用租户的库照样要清理：旧通知不会因为租户停用就不占空间
        var result = await databaseRunner.ForEachDatabaseAsync(ConnectionStringName, activeOnly: false, async (_, ct) =>
        {
            deleted += await DeleteCurrentDatabaseAsync(readCutoff, unreadCutoff, current.BatchSize, ct);
        }, cancellationToken);

        // 解析不出连接的租户与失败的库一样要让本轮失败：它们的通知一条都没清，
        // 把这一轮报成成功就没人知道有一批库被跳过了。抛出后调度器不记水位，
        // 本时段仍可被其他副本重试；积压由下一轮按截止时间扫描一并清掉
        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0)
        {
            throw new InvalidOperationException(
                $"Deleted {deleted} expired notification(s); {result.FailedDatabases.Count} of {result.Databases} database(s) failed, " +
                $"{result.UnresolvedTenants.Count} tenant(s) could not be resolved to a database.");
        }

        logger.LogInformation("Deleted {Count} expired notification(s) across {Databases} database(s).", deleted, result.Databases);
    }

    private async Task<int> DeleteCurrentDatabaseAsync(DateTime readCutoff, DateTime unreadCutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
            var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

            // 租户过滤器只放行当前上下文那一个租户，同库其余租户的通知不加 IgnoreQueryFilters 就永远留着
            var batch = await dbContext.Set<NotificationRecord>()
                .IgnoreQueryFilters()
                .Where(x => x.CreationTime < unreadCutoff || (x.IsRead && x.CreationTime < readCutoff))
                .OrderBy(x => x.CreationTime)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                return total;
            }

            dbContext.Set<NotificationRecord>().RemoveRange(batch);
            await unitOfWork.CompleteAsync(cancellationToken);

            total += batch.Count;
            if (batch.Count < batchSize)
            {
                return total;
            }
        }
    }
}
