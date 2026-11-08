using Leistd.BackgroundJobs.Recurring;
using Leistd.BackgroundJobs;
using Leistd.DependencyInjection.Extensions;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.EntityConfigurations;
using Leistd.EventBus.EntityFrameworkCore.Options;
using Leistd.EventBus.EntityFrameworkCore.Publishing;
using Leistd.EventBus.EntityFrameworkCore.Receiving;
using Leistd.EventBus.EntityFrameworkCore.Retention;
using Leistd.EventBus.EntityFrameworkCore.Stores;
using Leistd.EventBus.EntityFrameworkCore.TransportStatus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Leistd.EventBus.EntityFrameworkCore;

/// <summary>分布式事件 EF Core 注册与模型配置。</summary>
public static class DependencyInjection
{
    /// <summary>登记发件箱发布、持久接收与提交后处理；表只能属于同一个上下文。</summary>
    /// <remarks>宿主须提供工作单元、租户上下文、后台队列与一个传输；只写进程可显式关闭发送和接收。</remarks>
    /// <typeparam name="TDbContext">映射分布式事件表的业务上下文。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <example><code>services.AddDistributedEventBusEfCore&lt;AppDbContext&gt;();</code></example>
    public static IServiceCollection AddDistributedEventBusEfCore<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.EnsureSingleAuthoritative<IDistributedEventBus, EfCoreDistributedEventBus<TDbContext>>(ServiceLifetime.Transient,
            "Distributed event tables must belong to one DbContext.");
        services.EnsureSingleAuthoritative<IDistributedEventReceiver, EfCoreDistributedEventReceiver<TDbContext>>(ServiceLifetime.Transient,
            "Distributed event tables must belong to one DbContext.");
        if (!services.Any(x => x.ImplementationInstance is DistributedEventOptionsRegistration)) services.AddDistributedEventBus();
        services.TryAddTransient<IDistributedEventBus, EfCoreDistributedEventBus<TDbContext>>();
        services.TryAddScoped(_ => new EfCoreDistributedEventBus<TDbContext>.PublishedEvents());
        services.TryAddTransient<IDistributedEventReceiver, EfCoreDistributedEventReceiver<TDbContext>>();
        services.EnsureSingleAuthoritative<IDistributedEventManagementStore, EfCoreDistributedEventManagementStore<TDbContext>>(ServiceLifetime.Transient,
            "Distributed event tables must belong to one DbContext.");
        services.TryAddTransient<IDistributedEventManagementStore, EfCoreDistributedEventManagementStore<TDbContext>>();
        services.TryAddTransient<DistributedEventDatabaseScope<TDbContext>>();
        services.TryAddTransient<DistributedEventCatalog<TDbContext>>();
        services.TryAddTransient<DistributedEventRecovery<TDbContext>>();
        services.TryAddTransient<DistributedEventQueue<TDbContext>>();
        services.TryAddTransient<DistributedEventOutboxProcessor<TDbContext>>();
        services.TryAddTransient<DistributedEventInboxProcessor<TDbContext>>();
        services.TryAddTransient<DistributedEventScanner<TDbContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DistributedEventTransportStateReporter<TDbContext>>());
        services.AddRecurringJob<DistributedEventOutboxJob<TDbContext>>(DistributedEventOutboxJob<TDbContext>.Name,
            sp => RecurringJobSchedule.Every(sp.GetRequiredService<IOptions<DistributedEventOptions>>().Value.DispatchInterval),
            RecurringJobScope.Cluster, isEnabled: sp => sp.GetRequiredService<IOptionsSnapshot<DistributedEventOptions>>().Value.Outbox.DispatchEnabled);
        services.AddRecurringJob<DistributedEventInboxJob<TDbContext>>(DistributedEventInboxJob<TDbContext>.Name,
            sp => RecurringJobSchedule.Every(sp.GetRequiredService<IOptions<DistributedEventOptions>>().Value.DispatchInterval),
            RecurringJobScope.Cluster, isEnabled: sp => sp.GetRequiredService<IOptionsSnapshot<DistributedEventOptions>>().Value.Inbox.ProcessingEnabled);
        return services;
    }

    /// <summary>登记每日终态保留期任务，独立绑定保留期选项，默认保留十四天。</summary>
    /// <remarks>按最后状态变更时间清理，Pending 永不删除；去重窗口等于保留期。重复绑定须使用同一配置节。</remarks>
    /// <typeparam name="TDbContext">映射分布式事件表的业务上下文。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">绑定配置之后应用的选项。</param>
    /// <param name="configSectionPath">独立保留期配置节。</param>
    /// <example><code>services.AddDistributedEventRetention&lt;AppDbContext&gt;(options =&gt; options.Days = 30);</code></example>
    public static IServiceCollection AddDistributedEventRetention<TDbContext>(this IServiceCollection services,
        Action<DistributedEventRetentionOptions>? configure = null,
        string configSectionPath = DistributedEventRetentionOptions.SectionName) where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        var previous = services.Select(x => x.ImplementationInstance).OfType<DistributedEventRetentionOptionsValidator>().FirstOrDefault();
        if (previous is not null && previous.ConfigSectionPath != configSectionPath)
            throw new InvalidOperationException($"Distributed event retention already binds '{previous.ConfigSectionPath}'.");
        services.AddDistributedEventBusEfCore<TDbContext>();
        var options = services.AddOptions<DistributedEventRetentionOptions>().BindConfiguration(configSectionPath);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DistributedEventRetentionOptions>>(
            new DistributedEventRetentionOptionsValidator(configSectionPath)));
        services.AddRecurringJob<DistributedEventRetentionJob<TDbContext>>(DistributedEventRetentionJob<TDbContext>.Name,
            RecurringJobSchedule.DailyAt(new TimeOnly(2, 0)), RecurringJobScope.Cluster,
            isEnabled: sp => sp.GetRequiredService<IOptionsSnapshot<DistributedEventRetentionOptions>>().Value.Enabled);
        return services;
    }

    /// <summary>在业务上下文映射发件箱、收件箱、宿主集中拒收与传输状态表。</summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureDistributedEventBus(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DistributedEventOutboxConfiguration());
        modelBuilder.ApplyConfiguration(new DistributedEventInboxConfiguration());
        modelBuilder.ApplyConfiguration(new DistributedEventRejectionConfiguration());
        modelBuilder.ApplyConfiguration(new DistributedEventTransportStateConfiguration());
        return modelBuilder;
    }
}
