using Leistd.BackgroundJobs.Recurring;
using Leistd.OperationRecords.EntityFrameworkCore.Options;
using Leistd.Timing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.OperationRecords.EntityFrameworkCore.Retention;

// 保留期归档的周期任务：关着照常排期、到点跳过——开关可能被宿主设置随时打开，退出的话要重启才生效。
internal sealed class OperationRecordArchiveJob(
    IOptionsMonitor<OperationRecordRetentionOptions> retention,
    IOperationRecordArchiveService archiveService,
    IClock clock,
    ILogger<OperationRecordArchiveJob> logger) : IRecurringJob
{
    internal const string Name = "operation-records.archive";

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        // 越界的设置值在这里抛校验异常，按本轮失败记日志，不搬任何记录
        var current = retention.CurrentValue;
        if (!current.Enabled)
        {
            logger.LogDebug("Operation record archiving is disabled; skipping this run.");
            return;
        }

        var cutoff = clock.Now.AddDays(-current.RetentionDays);
        var result = await archiveService.ArchiveOlderThanAsync(cutoff, current.BatchSize, cancellationToken);

        if (result.FailedDatabases > 0)
        {
            // 抛出让调度器不记水位：失败的库最迟在下一个调度时段重做（按截止时间扫描，积压会一并搬走）
            throw new InvalidOperationException(
                $"Archived {result.Archived} operation record(s) created before {cutoff:o}; " +
                $"{result.FailedDatabases} of {result.Databases} database(s) failed.");
        }

        logger.LogInformation(
            "Archived {Count} operation record(s) created before {Cutoff:o} across {Databases} database(s).",
            result.Archived, cutoff, result.Databases);
    }
}
