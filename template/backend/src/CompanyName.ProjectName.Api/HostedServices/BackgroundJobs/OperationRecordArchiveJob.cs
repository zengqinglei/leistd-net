using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Infrastructure.OperationRecords;
using Leistd.Timing;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.HostedServices.BackgroundJobs;

/// <summary>
/// 操作记录到期归档：每天在配置的 UTC 时刻把超出保留期的记录搬入归档表。
/// </summary>
/// <remarks>
/// <para><b>默认不启用。</b>是否归档、保留几天可由宿主级设置（系统设置的「审计」面板）覆盖配置，
/// 每轮取 <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>，改完下一轮生效；未设置时就是配置文件的值，
/// 而配置默认关闭——审计表只增不减是安全的默认值。</para>
/// <para>执行时刻与批大小是部署调优参数，在构造时取一次：此时宿主设置还没加载进配置，
/// 启动期校验也已通过，排期不会因为一条不合规的设置而失效。</para>
/// <para><b>搬运而非删除</b>，理由见 <see cref="OperationRecordRetentionOptions"/>。</para>
/// </remarks>
public sealed class OperationRecordArchiveJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OperationRecordRetentionOptions> retention,
    IClock clock,
    ILogger<OperationRecordArchiveJob> logger) : BackgroundService
{
    // 执行时刻与批大小：构造时取一次（见类型说明）
    private readonly OperationRecordRetentionOptions _options = retention.CurrentValue;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 关着也照常排期：开关在设置里，管理员随时可能打开，启动时关着就退出的话要重启才能生效
        logger.LogInformation(
            "Operation record archiving is checked daily at {Hour:00}:00 UTC (batch size {BatchSize}); "
            + "whether it runs and the retention follow the host settings.",
            _options.DailyRunHourUtc, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(clock.Now);
            logger.LogDebug("Next operation record archiving run in {Minutes} minute(s).",
                (int)delay.TotalMinutes);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 停机，不是故障。
                break;
            }

            await RunOnceAsync(stoppingToken);
        }

        logger.LogInformation("Operation record archiving stopped.");
    }

    /// <summary>算出距下一个执行时刻的等待时长。</summary>
    /// <remarks>
    /// 已过今天的时刻就排到明天。<b>不用固定间隔</b>：那样每次重启都会把执行时间往后推，
    /// 运行一段时间后"凌晨跑"会漂移到业务高峰。
    /// </remarks>
    private TimeSpan DelayUntilNextRun(DateTime nowUtc)
    {
        var todayRun = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, _options.DailyRunHourUtc, 0, 0, DateTimeKind.Utc);
        var nextRun = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - nowUtc;
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 不合规的设置值（绕过接口写进库）在这里抛校验异常，按本轮失败记日志，不搬任何记录
            var current = retention.CurrentValue;
            if (!current.Enabled)
            {
                logger.LogDebug("Operation record archiving is disabled; skipping this run.");
                return;
            }

            var cutoff = clock.Now.AddDays(-current.RetentionDays);

            // 每轮一个独立 scope：本服务是单例，长期持有 scoped 的 DbContext 会让它
            // 活得和进程一样久，跟踪的实体只增不减。
            using var scope = scopeFactory.CreateScope();
            var archiveService = scope.ServiceProvider.GetRequiredService<IOperationRecordArchiveService>();

            var moved = await archiveService.ArchiveOlderThanAsync(
                cutoff, _options.BatchSize, stoppingToken);

            logger.LogInformation(
                "Archived {Count} operation record(s) created before {Cutoff:o}.", moved, cutoff);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 停机途中被取消，不是故障。
        }
        catch (Exception ex)
        {
            // 不让异常冒泡：ExecuteAsync 抛出后 BackgroundService 不会重启它，
            // 一次失败就等于此后再也不归档，而且没有任何后续日志能看出这一点。
            logger.LogError(ex, "Operation record archiving failed; will retry at the next scheduled run.");
        }
    }
}
