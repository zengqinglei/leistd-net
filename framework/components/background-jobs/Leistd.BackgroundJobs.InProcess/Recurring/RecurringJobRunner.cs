using Leistd.BackgroundJobs.Recurring;
using Leistd.Lock.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.BackgroundJobs.InProcess.Recurring;

// 一次执行：按范围决定是否抢锁、比对水位，再在新作用域里执行任务。
// 失败只记日志、不写水位，本轮不重试：下一次触发（最迟是下一个调度时段）会重做，因此任务必须幂等并扫描历史积压。
internal sealed class RecurringJobRunner(
    IServiceScopeFactory scopeFactory,
    IServiceProvider rootProvider,
    TimeProvider timeProvider,
    ILogger<RecurringJobRunner> logger)
{
    internal static string LockKey(string jobName) => $"background-jobs:{jobName}";

    public async Task<RecurringJobRunOutcome> RunOnceAsync(
        RecurringJobDefinition definition,
        RecurringJobSchedule schedule,
        CancellationToken stoppingToken)
    {
        var slot = schedule.GetSlot(timeProvider.GetUtcNow());

        try
        {
            if (definition.Scope == RecurringJobScope.EveryInstance)
            {
                await ExecuteAsync(definition, slot, stoppingToken);
                return RecurringJobRunOutcome.Completed;
            }

            var distributedLock = rootProvider.GetRequiredService<IDistributedLock>();

            // 零等待：别的副本正在跑这个时段，这一份直接让出，而不是排队等它跑完再重复一遍
            await using var handle = await distributedLock.TryLockAsync(LockKey(definition.Name), TimeSpan.Zero, stoppingToken);
            if (handle is null)
            {
                logger.LogDebug("Recurring job {Job} is running elsewhere; skipping slot {Slot:o}.", definition.Name, slot);
                return RecurringJobRunOutcome.SkippedLocked;
            }

            using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, handle.LockLost);
            await using var scope = scopeFactory.CreateAsyncScope();
            var stateStore = scope.ServiceProvider.GetRequiredService<IRecurringJobStateStore>();

            if (await stateStore.GetLastCompletedSlotAsync(definition.Name, run.Token) is { } completed && completed >= slot)
            {
                logger.LogDebug("Recurring job {Job} already completed slot {Slot:o}; skipping.", definition.Name, slot);
                return RecurringJobRunOutcome.SkippedCompleted;
            }

            await ExecuteAsync(definition, slot, run.Token);
            await stateStore.SetLastCompletedSlotAsync(definition.Name, slot, run.Token);
            return RecurringJobRunOutcome.Completed;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return RecurringJobRunOutcome.Canceled;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Recurring job {Job} lost its cluster lock during slot {Slot:o}; it will retry.", definition.Name, slot);
            return RecurringJobRunOutcome.Failed;
        }
        catch (Exception exception)
        {
            // 不让异常冒出调度循环：BackgroundService 的未处理异常默认会停掉整个宿主
            logger.LogError(exception, "Recurring job {Job} failed for slot {Slot:o}; it will retry.", definition.Name, slot);
            return RecurringJobRunOutcome.Failed;
        }
    }

    private async Task ExecuteAsync(RecurringJobDefinition definition, DateTimeOffset slot, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);
        await job.ExecuteAsync(new RecurringJobContext(definition.Name, slot), cancellationToken);
    }
}

internal enum RecurringJobRunOutcome
{
    Completed,
    SkippedLocked,
    SkippedCompleted,
    Failed,
    Canceled
}
