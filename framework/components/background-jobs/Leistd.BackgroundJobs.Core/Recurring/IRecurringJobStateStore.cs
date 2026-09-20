namespace Leistd.BackgroundJobs.Recurring;

/// <summary>
/// 记录 <see cref="RecurringJobScope.Cluster"/> 任务最近成功完成的时段。
/// </summary>
/// <remarks>
/// 只靠分布式锁只能防止同一时段<b>并发</b>执行，防不住<b>先后</b>重复：副本 A 执行完释放锁后，
/// 时钟稍慢的副本 B 在同一时段醒来照样能拿到锁。水位让 B 看到"这个时段已经做过"。
/// 多副本部署必须使用共享存储的实现（如 EF 实现）；进程内默认实现只对单副本成立。
/// </remarks>
public interface IRecurringJobStateStore
{
    /// <summary>读取任务最近成功完成的时段；从未完成时为 <see langword="null"/>。</summary>
    /// <param name="jobName">任务名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DateTimeOffset?> GetLastCompletedSlotAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>记录任务成功完成了某个时段。</summary>
    /// <param name="jobName">任务名。</param>
    /// <param name="slot">时段起点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetLastCompletedSlotAsync(string jobName, DateTimeOffset slot, CancellationToken cancellationToken = default);
}
