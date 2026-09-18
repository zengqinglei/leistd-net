namespace CompanyName.ProjectName.Api.HostedServices.Workers;

/// <summary>
/// 后台任务队列：把不该占着请求线程的工作挪到进程内的后台消费者上。
/// </summary>
/// <remarks>
/// <para><b>这是进程内队列，不是消息中间件。</b>进程退出、崩溃或被调度器驱逐时，
/// 尚未消费的工作项<b>会丢失</b>。因此只放「丢了也只是少做一次」的工作——
/// 缓存预热、非关键通知、统计汇总。<b>不要放</b>必须发生的事：审计记录、
/// 账务变更、对外承诺过的回调，那些要么同事务写库，要么走真正的持久化队列。</para>
/// <para><b>有界队列 + 背压</b>：容量满时 <see cref="QueueAsync"/> 会等待而不是丢弃，
/// 把压力如实回传给生产者。无界队列在生产快于消费时只会把问题推迟到内存耗尽，
/// 且那时已经无从判断是谁堆的。</para>
/// </remarks>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// 入队一个工作项；队列满时等待直到有空位。
    /// </summary>
    /// <param name="workItem">
    /// 要执行的工作。<b>它拿到的是一个新建的 scope 的 <see cref="IServiceProvider"/></b>——
    /// 后台服务本身是单例，直接闭包捕获请求期的 scoped 服务会在执行时拿到已释放的实例。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, ValueTask> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试立即入队；队列已满时返回 <see langword="false"/> 而不等待。
    /// </summary>
    /// <remarks>
    /// 给「宁可不做也不能拖慢当前请求」的调用点用。<b>返回值必须处理</b>：
    /// 忽略它就等于在队列满时静默丢弃工作，而那正是最需要知道的时刻。
    /// </remarks>
    /// <param name="workItem">要执行的工作。</param>
    /// <returns>是否成功入队。</returns>
    bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem);
}
