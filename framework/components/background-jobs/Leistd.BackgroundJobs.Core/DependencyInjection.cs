using Leistd.BackgroundJobs.Recurring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.BackgroundJobs;

/// <summary>
/// 周期任务的登记入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 登记一个周期任务。
    /// </summary>
    /// <remarks>
    /// <para>只登记描述，不启动调度：宿主还需注册一个调度器实现（如 <c>AddInProcessBackgroundJobs()</c>）。
    /// 组件在自己的 <c>Add*</c> 里登记维护任务，调度器由宿主选。</para>
    /// <para>任务名全局唯一：同名同类型重复登记是幂等的，同名不同类型在登记时抛出——
    /// 名字同时是集群锁与水位的键，两个任务共用一个名字会互相跳过。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddRecurringJob&lt;LeadRecycleJob&gt;(
    ///     "crm.lead-recycle",
    ///     RecurringJobSchedule.DailyAt(new TimeOnly(18, 0)),
    ///     RecurringJobScope.Cluster);
    /// </code>
    /// </example>
    /// <typeparam name="TJob">任务类型，按瞬态注册，每次执行在新作用域里解析。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="name">任务名，全局唯一，建议带组件前缀（如 <c>operation-records.archive</c>）。</param>
    /// <param name="schedule">排期。</param>
    /// <param name="scope">多副本下的执行范围，必须显式给出。</param>
    public static IServiceCollection AddRecurringJob<TJob>(
        this IServiceCollection services,
        string name,
        RecurringJobSchedule schedule,
        RecurringJobScope scope)
        where TJob : class, IRecurringJob
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return services.AddRecurringJob<TJob>(name, _ => schedule, scope);
    }

    /// <summary>
    /// 登记一个周期任务，排期在调度器启动时从容器取（如来自选项）。
    /// </summary>
    /// <remarks>规则同另一个重载。排期只在调度器启动时取一次，改选项后要重启才影响排期。</remarks>
    /// <typeparam name="TJob">任务类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="name">任务名，全局唯一。</param>
    /// <param name="scheduleFactory">从容器取排期。</param>
    /// <param name="scope">多副本下的执行范围，必须显式给出。</param>
    public static IServiceCollection AddRecurringJob<TJob>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, RecurringJobSchedule> scheduleFactory,
        RecurringJobScope scope)
        where TJob : class, IRecurringJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scheduleFactory);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "The recurring job scope must be chosen explicitly.");
        }

        var existing = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RecurringJobDefinition>()
            .FirstOrDefault(definition => string.Equals(definition.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.JobType == typeof(TJob))
            {
                return services;
            }

            throw new InvalidOperationException(
                $"Recurring job '{name}' is already registered for '{existing.JobType.FullName}'; job names must be unique.");
        }

        services.TryAddTransient<TJob>();
        services.AddSingleton(new RecurringJobDefinition(name, typeof(TJob), scope, scheduleFactory));
        return services;
    }
}
