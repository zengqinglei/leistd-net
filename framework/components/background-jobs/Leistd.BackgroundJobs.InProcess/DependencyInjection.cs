using Leistd.BackgroundJobs.InProcess.Options;
using Leistd.BackgroundJobs.InProcess.Queues;
using Leistd.BackgroundJobs.InProcess.Recurring;
using Leistd.BackgroundJobs.Options;
using Leistd.BackgroundJobs.Queues;
using Leistd.BackgroundJobs.Recurring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Leistd.BackgroundJobs.InProcess;

/// <summary>
/// 进程内后台作业实现的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册进程内调度器与后台任务队列。
    /// </summary>
    /// <remarks>
    /// <para>调度器执行所有经 <c>AddRecurringJob&lt;TJob&gt;</c> 登记的任务；登记与本方法的调用顺序无关。
    /// 选项绑定 <c>Leistd:BackgroundJobs</c> 与 <c>Leistd:BackgroundJobs:InProcess</c>。</para>
    /// <para>集群任务需要 <c>IDistributedLock</c>，缺失时宿主启动失败。水位默认存在进程内，
    /// 多副本部署再注册共享存储的实现（如 <c>AddBackgroundJobsEfCore&lt;TDbContext&gt;()</c>）。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisDistributedLock(redisConnectionString);
    /// builder.Services.AddInProcessBackgroundJobs();
    /// builder.Services.AddRecurringJob&lt;LeadRecycleJob&gt;(
    ///     "crm.lead-recycle", RecurringJobSchedule.DailyAt(new TimeOnly(18, 0)), RecurringJobScope.Cluster);
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    public static IServiceCollection AddInProcessBackgroundJobs(
        this IServiceCollection services,
        Action<BackgroundJobOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BackgroundJobOptions>().BindConfiguration(BackgroundJobOptions.SectionName);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<InProcessBackgroundJobOptions>, InProcessBackgroundJobOptionsValidator>());
        services.AddOptions<InProcessBackgroundJobOptions>()
            .BindConfiguration(InProcessBackgroundJobOptions.SectionName)
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRecurringJobStateStore, InMemoryRecurringJobStateStore>();

        // 幂等：组合根拆分时重复调用是常态，托管服务注册两遍会让每个任务每个时段跑两次
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RecurringJobRunner)))
        {
            return services;
        }

        services.AddSingleton<RecurringJobRunner>();
        services.AddHostedService<RecurringJobScheduler>();

        // 队列与消费者必须是同一个通道：两处各注册一个实例，生产者写进一个、消费者读另一个，工作项永不执行
        services.AddSingleton<BackgroundTaskQueue>();
        services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
        services.AddHostedService<BackgroundTaskQueueWorker>();
        return services;
    }
}
