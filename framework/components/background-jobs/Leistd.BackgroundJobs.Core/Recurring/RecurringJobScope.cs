namespace Leistd.BackgroundJobs.Recurring;

/// <summary>
/// 多副本部署下，一个时段里由谁执行。
/// </summary>
/// <remarks>
/// 登记时必须显式选择，没有默认值：维护数据的任务选错成每副本执行会重复处理，
/// 刷新进程内状态的任务选错成全集群一份会让其余副本永远拿不到新值——两种错误都不报错。
/// </remarks>
public enum RecurringJobScope
{
    /// <summary>
    /// 全集群每个时段只执行一次：执行前抢分布式锁（抢不到即跳过），并比对已完成时段的水位。
    /// 用于清理、归档、扫描这类作用在共享数据上的任务。
    /// </summary>
    Cluster = 1,

    /// <summary>
    /// 每个副本各自执行：不加锁、不记水位。用于刷新本进程内状态的任务（缓存、配置）。
    /// </summary>
    EveryInstance = 2
}
