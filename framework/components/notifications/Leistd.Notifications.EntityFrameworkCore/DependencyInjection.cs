using Microsoft.Extensions.Options;
using Leistd.Notifications.EntityFrameworkCore.Retention;
using Leistd.Notifications.EntityFrameworkCore.Options;
using Leistd.BackgroundJobs.Recurring;
using Leistd.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Notifications.EntityFrameworkCore.EntityConfigurations;
using Leistd.Notifications.EntityFrameworkCore.Stores;
using Leistd.Notifications.Abstractions;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// 通知 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 通知持久化存储（基于指定 DbContext）。
    /// </summary>
    /// <remarks>
    /// 宿主须注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>；
    /// 本存储通过 <c>IDbContextProvider&lt;TDbContext&gt;</c> 获取绑定连接的上下文。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotificationsEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射通知表
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     base.OnModelCreating(modelBuilder);
    ///     modelBuilder.ConfigureNotifications();
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddNotificationsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {

        // 发布器只消费一个权威存储：两个上下文各注册一次时会静默取一条，通知落进宿主没预期的库。
        services.EnsureSingleAuthoritative<INotificationStore, EfCoreNotificationStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Notifications have a single authoritative store; map NotificationRecord in one DbContext.");

        services.TryAddTransient<INotificationStore, EfCoreNotificationStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 启用通知保留期：到期通知每天按物理库逐个删除，作为集群周期任务执行。
    /// </summary>
    /// <remarks>
    /// <para>选项绑定 <c>Leistd:Notifications:Retention</c> 并在启动期校验；默认开启，已读保留 90 天、未读保留 365 天，
    /// 均按创建时间计。开关与天数每轮取当前值，执行时刻只在排期时取一次。</para>
    /// <para>需要后台作业调度器（如 <c>AddInProcessBackgroundJobs()</c>）与分布式锁。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotificationsEfCore&lt;AppDbContext&gt;();
    /// builder.Services.AddNotificationRetention&lt;AppDbContext&gt;();
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载通知表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    public static IServiceCollection AddNotificationRetention<TDbContext>(
        this IServiceCollection services,
        Action<NotificationRetentionOptions>? configure = null)
        where TDbContext : DbContext
    {
        services.AddOptions<NotificationRetentionOptions>()
            .BindConfiguration(NotificationRetentionOptions.SectionName)
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NotificationRetentionOptions>, NotificationRetentionOptionsValidator>());
        services.AddRecurringJob<NotificationRetentionJob<TDbContext>>(
            NotificationRetentionJob<TDbContext>.Name,
            sp => RecurringJobSchedule.DailyAt(new TimeOnly(
                sp.GetRequiredService<IOptions<NotificationRetentionOptions>>().Value.DailyRunHourUtc, 0)),
            RecurringJobScope.Cluster);
        return services;
    }

    /// <summary>
    /// 将 NotificationRecord 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    public static ModelBuilder ConfigureNotifications(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        return modelBuilder;
    }

}
