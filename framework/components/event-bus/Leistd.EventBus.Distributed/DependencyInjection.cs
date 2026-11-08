using Leistd.DependencyInjection.Extensions;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.EventHandlers;
using Leistd.EventBus.Distributed.ExceptionMappings;
using Leistd.EventBus.Distributed.Loopback;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Leistd.ExceptionHandling.Options;
using Leistd.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.Distributed;

/// <summary>分布式事件选项、订阅和回环传输的注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>绑定部署选项并登记订阅、错误码和内置测试消费者；启动时校验分工与传输配置。</summary>
    /// <remarks>还须注册存储与一个传输。无发送、无接收的只写进程可不注册传输；重复调用必须使用同一配置节。</remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">配置节之后应用的配置。</param>
    /// <param name="configSectionPath">绑定的配置节。</param>
    /// <example><code>
    /// services.AddDistributedEventBus();
    /// services.AddDistributedEventBusEfCore&lt;AppDbContext&gt;();
    /// services.AddRabbitMqEventTransport();
    /// </code></example>
    public static IServiceCollection AddDistributedEventBus(
        this IServiceCollection services,
        Action<DistributedEventOptions>? configure = null,
        string configSectionPath = DistributedEventOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        var registered = services.Select(x => x.ImplementationInstance).OfType<DistributedEventOptionsRegistration>().FirstOrDefault();
        if (registered is not null && registered.ConfigSectionPath != configSectionPath)
        {
            throw new InvalidOperationException($"AddDistributedEventBus() already binds '{registered.ConfigSectionPath}'; it cannot also bind '{configSectionPath}'.");
        }
        if (registered is null)
        {
            services.AddSingleton(new DistributedEventOptionsRegistration(configSectionPath));
            services.AddSingleton<IValidateOptions<DistributedEventOptions>>(sp => new DistributedEventOptionsValidator(
                configSectionPath, sp.GetRequiredService<IServiceProviderIsService>()));
            services.AddJsonLocalizationResources(typeof(DistributedEventErrorCodes).Assembly);
            services.Configure<GlobalExceptionOptions>(DistributedEventExceptionMappings.Configure);
        }

        var options = services.AddOptions<DistributedEventOptions>().BindConfiguration(configSectionPath);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddTransient<IDistributedEventManagementService, DistributedEventManagementService>();
        services.TryAddSingleton<IDistributedEventSubscriptions, DistributedEventSubscriptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DistributedEventStartup>());
        services.AddDistributedEventHandler<DistributedEventTestHandler>(DistributedEventTestEvent.ConsumerName);
        return services;
    }

    /// <summary>登记处理器实现的分布式事件订阅，不扫描程序集，也不将它登记给本地总线。</summary>
    /// <remarks>消费者名参与去重；处理器更名时应显式保留原名。同一事件名称不能映射到不同的 CLR 类型。</remarks>
    /// <typeparam name="THandler">实现一个或多个 <see cref="IEventHandler{TEvent}"/> 的具体处理器。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="consumerName">稳定消费者名；省略时使用处理器类型全名。</param>
    public static IServiceCollection AddDistributedEventHandler<THandler>(this IServiceCollection services, string? consumerName = null)
        where THandler : class
    {
        var handler = typeof(THandler);
        var consumer = consumerName ?? handler.FullName;
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        var eventTypes = handler.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(x => x.GetGenericArguments()[0])
            .Where(x => typeof(IDistributedEvent).IsAssignableFrom(x)).Distinct().ToArray();
        if (handler.IsAbstract || eventTypes.Length == 0 || eventTypes.Any(x => x.IsAbstract || x.IsInterface))
        {
            throw new ArgumentException($"Handler '{handler}' must implement IEventHandler<TEvent> for a concrete distributed event.", nameof(THandler));
        }
        services.TryAddTransient<THandler>();
        foreach (var type in eventTypes)
        {
            services.AddSingleton(new DistributedEventSubscription
            {
                EventName = DistributedEventNames.Get(type), EventType = type, HandlerType = handler, Consumer = consumer
            });
        }
        return services;
    }

    /// <summary>选择进程内回环传输，用于测试和单进程开发；与其他传输互斥。</summary>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddLoopbackEventTransport(this IServiceCollection services)
    {
        services.EnsureSingleAuthoritative<IDistributedEventTransport, LoopbackEventTransport>(ServiceLifetime.Singleton,
            "Only one distributed event transport can be registered.");
        var registered = services.Any(x => x.ServiceType == typeof(LoopbackRegistration));
        services.TryAddSingleton<IDistributedEventTransport, LoopbackEventTransport>();
        if (!registered)
        {
            services.AddSingleton<LoopbackRegistration>();
            services.AddHealthChecks().AddCheck("event-bus-loopback", () => HealthCheckResult.Healthy(), tags: ["event-bus"]);
        }
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
    private sealed class LoopbackRegistration;
}

internal sealed class DistributedEventStartup(IOptions<DistributedEventOptions> options, IDistributedEventSubscriptions subscriptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = options.Value;
        _ = subscriptions.GetAll();
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
