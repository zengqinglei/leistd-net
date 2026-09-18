using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Infrastructure.OperationRecords;
using Leistd.Timing;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.HostedServices.BackgroundServices;

/// <summary>
/// 操作记录到期归档：每天在配置的 UTC 时刻把超出保留期的记录搬入归档表。
/// </summary>
/// <remarks>
/// <para><b>默认不启用。</b>未在配置里显式打开时，本服务启动后立即退出并记一条日志——
/// 审计表只增不减是安全的默认值。</para>
/// <para><b>搬运而非删除</b>，理由见 <see cref="OperationRecordRetentionOptions"/>。</para>
/// </remarks>
public sealed class OperationRecordArchiveBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OperationRecordRetentionOptions> options,
    IClock clock,
    ILogger<OperationRecordArchiveBackgroundService> logger) : BackgroundService
{
    private readonly OperationRecordRetentionOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Operation record archiving is disabled; records will be kept indefinitely.");
            return;
        }

        logger.LogInformation(
            "Operation record archiving enabled: retention {RetentionDays} day(s), "
            + "daily at {Hour:00}:00 UTC, batch size {BatchSize}.",
            _options.RetentionDays, _options.DailyRunHourUtc, _options.BatchSize);

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
        var cutoff = clock.Now.AddDays(-_options.RetentionDays);

        try
        {
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
