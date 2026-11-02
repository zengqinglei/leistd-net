using Leistd.BackgroundJobs;
using Leistd.BackgroundJobs.Recurring;
using Leistd.DependencyInjection.Extensions;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;
using Leistd.Webhooks.EntityFrameworkCore.Management;
using Leistd.Webhooks.EntityFrameworkCore.Options;
using Leistd.Webhooks.EntityFrameworkCore.Publishing;
using Leistd.Webhooks.EntityFrameworkCore.Retention;
using Leistd.Webhooks.EntityFrameworkCore.Stores;
using Leistd.Webhooks.EntityFrameworkCore.Subscriptions;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Publishing;
using Leistd.Webhooks.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore;

/// <summary>webhook EF Core 持久化的注册与模型配置。</summary>
public static class DependencyInjection
{
    /// <summary>注册 webhook 的 EF Core 持久化：发布器（<see cref="IWebhookPublisher"/>）、提交后立即投递、投递统计
    /// （<see cref="IWebhookDeliveryReader"/>）、管理用例（<see cref="IWebhookManagementService"/>，<c>MapWebhooks</c> 依赖它），并登记集群周期任务 <c>webhooks.dispatch</c>（兜底扫描，间隔取
    /// <c>Leistd:Webhooks:DispatchInterval</c>）。</summary>
    /// <remarks>
    /// <para>宿主没有调用过 Core 的 <c>AddWebhooks()</c> 时以默认配置节代为调用；要改配置节，在本方法之前调用它。</para>
    /// <para>存储经 <c>IDbContextProvider&lt;TDbContext&gt;</c> 取上下文，宿主须注册 <c>AddUnitOfWork()</c> 与
    /// <c>AddUnitOfWorkEfCore()</c>；还需要 <c>AddMultiTenancyCore()</c>（扫描逐库进入，单库时只有宿主库）、
    /// 后台作业调度器与队列（如 <c>AddInProcessBackgroundJobs()</c>）、分布式锁，以及宿主的
    /// <c>AddDataProtection()</c>（签名密钥加密存储，多副本须共享密钥环）。</para>
    /// <para>订阅、事件与投递实现 <c>IMultiTenant</c>，租户隔离由宿主上下文的全局查询过滤器承担（继承
    /// <c>BaseDbContext</c> 即有）。</para>
    /// <para>可重复调用；以另一个 DbContext 重复调用时抛出 <see cref="InvalidOperationException"/>：webhook 表只映射在一个上下文里。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddDataProtection();
    /// builder.Services.AddMultiTenancyCore();
    /// builder.Services.AddInProcessBackgroundJobs();
    /// builder.Services.AddWebhooksEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射 webhook 表
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     base.OnModelCreating(modelBuilder);
    ///     modelBuilder.ConfigureWebhooks();
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载 webhook 表的 DbContext，须在 OnModelCreating 里调用 <see cref="ConfigureWebhooks"/>。</typeparam>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddWebhooksEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        const string reason = "Webhook tables are mapped in a single DbContext; call AddWebhooksEfCore<TDbContext>() for that context only.";

        // 发布、统计与管理各只消费一个权威存储：两个上下文各注册一次时会静默取其一
        services.EnsureSingleAuthoritative<IWebhookPublisher, EfCoreWebhookPublisher<TDbContext>>(ServiceLifetime.Transient, reason);
        services.EnsureSingleAuthoritative<IWebhookDeliveryReader, EfCoreWebhookDeliveryReader<TDbContext>>(ServiceLifetime.Transient, reason);
        services.EnsureSingleAuthoritative<IWebhookManagementService, EfCoreWebhookManagementService<TDbContext>>(ServiceLifetime.Transient, reason);

        // 宿主先以另一配置节调用过 AddWebhooks 时沿用它，不以默认节再调一次（那会因配置节不同而抛出）
        if (!services.Any(descriptor => descriptor.ImplementationInstance is WebhookOptionsValidator))
        {
            services.AddWebhooks();
        }

        services.TryAddTransient<IWebhookPublisher, EfCoreWebhookPublisher<TDbContext>>();
        services.TryAddTransient<IWebhookDeliveryReader, EfCoreWebhookDeliveryReader<TDbContext>>();
        services.TryAddTransient<IWebhookManagementService, EfCoreWebhookManagementService<TDbContext>>();
        services.TryAddSingleton<WebhookSecretProtector>();
        services.TryAddTransient<WebhookSubscriptionDeactivator<TDbContext>>();
        services.TryAddTransient<WebhookDeliveryQueue<TDbContext>>();
        services.TryAddTransient<WebhookDeliveryProcessor<TDbContext>>();

        services.AddRecurringJob<WebhookDispatchJob<TDbContext>>(
            WebhookDispatchJob<TDbContext>.Name,
            sp => RecurringJobSchedule.Every(sp.GetRequiredService<IOptions<WebhookOptions>>().Value.DispatchInterval),
            RecurringJobScope.Cluster);
        return services;
    }

    /// <summary>启用 webhook 记录保留期：到期的事件、投递与尝试每天按物理库逐个删除，作为集群周期任务 <c>webhooks.retention</c> 执行。</summary>
    /// <remarks>
    /// <para>按投递创建时间计：删除早于保留期的投递及其尝试，再删除早于保留期、已没有投递的事件；开关与天数每轮取当前值，
    /// 执行时刻只在排期时取一次。需要后台作业调度器、分布式锁与 <c>AddMultiTenancyCore()</c>。</para>
    /// <para>选项绑定 <paramref name="configSectionPath"/>（默认 <c>Leistd:Webhooks:Retention</c>）并在启动期校验，
    /// 重复调用换用另一配置节时抛出 <see cref="InvalidOperationException"/>；默认开启，保留 62 天。</para>
    /// </remarks>
    /// <typeparam name="TDbContext">承载 webhook 表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    /// <param name="configSectionPath">选项绑定的配置节，校验消息按它报键名。</param>
    public static IServiceCollection AddWebhookRetention<TDbContext>(
        this IServiceCollection services,
        Action<WebhookRetentionOptions>? configure = null,
        string configSectionPath = WebhookRetentionOptions.SectionName)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);

        // 选项只有一份：换用另一配置节的重复调用会让校验消息报错键名
        if (services.Select(descriptor => descriptor.ImplementationInstance).OfType<WebhookRetentionOptionsValidator>().FirstOrDefault()
                is { } registered && registered.ConfigSectionPath != configSectionPath)
        {
            throw new InvalidOperationException(
                $"AddWebhookRetention() already binds '{registered.ConfigSectionPath}'; it cannot also bind '{configSectionPath}'.");
        }

        var options = services.AddOptions<WebhookRetentionOptions>().BindConfiguration(configSectionPath);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<WebhookRetentionOptions>>(
            new WebhookRetentionOptionsValidator(configSectionPath)));

        // 统计据此报告保留期下限：只有登记了保留期任务，早于下限的记录才会被删除
        services.TryAddSingleton<WebhookRetentionRegistration>();
        services.AddRecurringJob<WebhookRetentionJob<TDbContext>>(
            WebhookRetentionJob<TDbContext>.Name,
            sp => RecurringJobSchedule.DailyAt(new TimeOnly(
                sp.GetRequiredService<IOptions<WebhookRetentionOptions>>().Value.DailyRunHourUtc, 0)),
            RecurringJobScope.Cluster,
            isEnabled: sp => sp.GetRequiredService<IOptionsSnapshot<WebhookRetentionOptions>>().Value.Enabled);
        return services;
    }

    /// <summary>映射 webhook 的订阅、事件、投递与尝试四张表，在 OnModelCreating 中调用。</summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureWebhooks(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEventConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookAttemptConfiguration());
        return modelBuilder;
    }
}
