using System.Threading.Channels;

namespace CompanyName.ProjectName.Api.HostedServices.Workers;

/// <summary>
/// 进程内后台任务队列的消费者：一个实例同时是队列本身与消费它的后台服务。
/// </summary>
/// <remarks>
/// <para><b>一个实例、两处注册。</b>它既要被注入成 <see cref="IBackgroundTaskQueue"/> 供生产者入队，
/// 又要作为托管服务被主机启动。两处各注册一次会得到<b>两个实例</b>——生产者写进 A 的队列，
/// 而被启动消费的是 B，工作项永远不会执行，且没有任何报错。注册方式见 <c>Program.cs</c>。</para>
/// <para><b>队列是有界的。</b>容量满时 <see cref="QueueAsync"/> 等待、
/// <see cref="TryQueue"/> 返回 <see langword="false"/>，把压力如实回传给生产者。
/// 无界队列只是把"生产快于消费"推迟到内存耗尽才暴露，那时已无从判断是谁堆的。</para>
/// <para><b>单项失败不停服。</b>每个工作项各自 try/catch：<c>BackgroundService</c> 的
/// <c>ExecuteAsync</c> 一旦抛出就再也不会被重新调度，后续所有工作项都会静默不执行。</para>
/// </remarks>
public sealed class BackgroundTaskWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundTaskWorker> logger) : BackgroundService, IBackgroundTaskQueue
{
    /// <summary>队列容量。</summary>
    /// <remarks>
    /// 取值依据是"堆积到这个数就该让生产者慢下来"，不是"能放多少"。
    /// 调大它只会让问题更晚暴露，而暴露时堆积的内存也更多。
    /// </remarks>
    public const int QueueCapacity = 256;

    private readonly Channel<Func<IServiceProvider, CancellationToken, ValueTask>> _channel =
        Channel.CreateBounded<Func<IServiceProvider, CancellationToken, ValueTask>>(
            new BoundedChannelOptions(QueueCapacity)
            {
                // 满了就等，不丢也不抛——这就是背压。
                FullMode = BoundedChannelFullMode.Wait,
                // 只有 ExecuteAsync 一个读者；写者是任意请求线程。
                SingleReader = true,
                SingleWriter = false,
            });

    /// <inheritdoc />
    public ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    /// <inheritdoc />
    public bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.TryWrite(workItem);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Background task worker started with a bounded queue of {Capacity} items.", QueueCapacity);

        try
        {
            await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // 每项一个独立 scope：本服务是单例，直接用它的 IServiceProvider 取 scoped 服务
                    // 会拿到根容器里的实例，DbContext 之类会被跨请求共用并一直不释放。
                    using var scope = scopeFactory.CreateScope();
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机途中被取消，不是故障。
                    break;
                }
                catch (Exception ex)
                {
                    // 吞掉单项异常是<b>刻意的</b>：让它冒泡会终止 ExecuteAsync，
                    // 而 BackgroundService 不会重启它——那等于一个坏工作项让整个队列永久停摆。
                    logger.LogError(ex, "A background work item failed and was discarded.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机。
        }

        logger.LogInformation("Background task worker stopped.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// 先关闭写入口，不再接收新工作项；随后交给基类取消 <c>stoppingToken</c>。
    /// <b>语义要说清</b>：正在执行的那一项会收到取消令牌并被等待完成，
    /// 而<b>队列中尚未开始的项会被丢弃</b>。这是进程内队列的固有性质，
    /// 所以必须发生的工作不能放进来（见 <see cref="IBackgroundTaskQueue"/>）。
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
