using Leistd.AmbientContext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Leistd.BackgroundJobs.InProcess.Queues;

// 队列消费者。单项失败不停服：ExecuteAsync 一旦抛出就不会再被调度，后续工作项全部静默不执行。
// 停机时先关写入口；正在执行的一项收到取消令牌，尚未开始的项被丢弃（进程内队列的固有性质）。
internal sealed class BackgroundTaskQueueWorker(
    BackgroundTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundTaskQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await RunAsync(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task RunAsync(QueuedWorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            using (item.Context is { } context
                       ? scope.ServiceProvider.GetRequiredService<IAmbientContext>().Restore(context)
                       : null)
            {
                await item.WorkItem(scope.ServiceProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A background work item failed and was discarded.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}
