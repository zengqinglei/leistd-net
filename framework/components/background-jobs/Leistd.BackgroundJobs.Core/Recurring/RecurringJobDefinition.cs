namespace Leistd.BackgroundJobs.Recurring;

/// <summary>
/// 一个登记过的周期任务，供调度器实现读取。
/// </summary>
/// <remarks>由 <c>AddRecurringJob&lt;TJob&gt;</c> 生成；业务代码不直接构造。</remarks>
public sealed class RecurringJobDefinition
{
    internal RecurringJobDefinition(
        string name,
        Type jobType,
        RecurringJobScope scope,
        Func<IServiceProvider, RecurringJobSchedule> scheduleFactory)
    {
        Name = name;
        JobType = jobType;
        Scope = scope;
        ScheduleFactory = scheduleFactory;
    }

    /// <summary>任务名，全局唯一；也是集群锁与水位的键。</summary>
    public string Name { get; }

    /// <summary>实现 <see cref="IRecurringJob"/> 的任务类型。</summary>
    public Type JobType { get; }

    /// <summary>多副本下的执行范围。</summary>
    public RecurringJobScope Scope { get; }

    /// <summary>从容器取排期；调度器启动时调用一次，排期可以来自选项。</summary>
    public Func<IServiceProvider, RecurringJobSchedule> ScheduleFactory { get; }
}
