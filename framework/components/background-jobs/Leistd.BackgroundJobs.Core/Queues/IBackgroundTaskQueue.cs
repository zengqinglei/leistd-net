namespace Leistd.BackgroundJobs.Queues;

/// <summary>
/// 进程内后台任务队列：把不该占着请求的工作挪到后台消费者上执行。
/// </summary>
/// <remarks>
/// <para><b>不持久。</b>进程退出、崩溃或被调度器驱逐时，尚未执行的工作项会丢失。只放"丢了也只是少做一次"的工作
/// （非关键通知、缓存预热）；必须发生的事要么与业务同事务写库，要么走持久化作业。</para>
/// <para><b>有界、背压。</b>队列满时 <see cref="QueueAsync"/> 等待、<see cref="TryQueue"/> 返回
/// <see langword="false"/>，由调用方二选一，没有默认的丢弃策略。</para>
/// <para><b>上下文随任务带过去。</b>入队时的主体、租户与链路标识在执行时还原；工作单元与请求对象不随行。
/// 工作项拿到的是新建作用域的 <see cref="IServiceProvider"/>，不要闭包捕获请求期的 Scoped 服务。</para>
/// </remarks>
public interface IBackgroundTaskQueue
{
    /// <summary>入队一个工作项；队列满时等待直到有空位。</summary>
    /// <param name="workItem">要执行的工作，参数是执行时新建作用域的服务提供器与停机令牌。</param>
    /// <param name="cancellationToken">等待入队的取消令牌。</param>
    ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>尝试立即入队；队列已满时返回 <see langword="false"/> 而不等待。</summary>
    /// <remarks>返回值必须处理：忽略它就等于在队列满时静默丢弃工作。</remarks>
    /// <param name="workItem">要执行的工作。</param>
    /// <returns>是否入队成功。</returns>
    bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem);
}
