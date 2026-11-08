using Leistd.DependencyInjection.Extensions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.RabbitMQ;

/// <summary>RabbitMQ 分布式事件传输注册。</summary>
public static class DependencyInjection
{
    /// <summary>选择 RabbitMQ 传输，登记持久单活队列消费。</summary>
    /// <remarks>连接 URI 从 ConnectionStrings 读取；队列名默认为宿主应用名。只在持久接收成功后确认消息，取消与失败不越过未确认消息。</remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">绑定配置后应用的选项。</param>
    /// <param name="configSectionPath">传输配置节。</param>
    /// <example><code>services.AddRabbitMqEventTransport();</code></example>
    public static IServiceCollection AddRabbitMqEventTransport(this IServiceCollection services,
        Action<RabbitMqEventTransportOptions>? configure = null,
        string configSectionPath = RabbitMqEventTransportOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        services.EnsureSingleAuthoritative<IDistributedEventTransport, RabbitMqEventTransport>(ServiceLifetime.Singleton,
            "Only one distributed event transport can be registered.");
        var previous = services.Select(x => x.ImplementationInstance).OfType<RabbitMqEventTransportRegistration>().FirstOrDefault();
        if (previous is not null && previous.Path != configSectionPath) throw new InvalidOperationException("RabbitMQ event transport is already bound to another configuration section.");
        if (previous is null)
        {
            services.AddSingleton(new RabbitMqEventTransportRegistration(configSectionPath));
            services.AddSingleton<IValidateOptions<RabbitMqEventTransportOptions>>(sp => new RabbitMqEventTransportOptionsValidator(
                configSectionPath, sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        }
        var options = services.AddOptions<RabbitMqEventTransportOptions>().BindConfiguration(configSectionPath);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddSingleton<IDistributedEventTransport, RabbitMqEventTransport>();
        services.TryAddSingleton(sp => (RabbitMqEventTransport)sp.GetRequiredService<IDistributedEventTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RabbitMqEventTransportWorker>());
        return services;
    }

    /// <summary>登记传输健康检查，附加宿主标签并始终保留 event-bus 标签；重复调用合并标签。</summary>
    /// <param name="builder">健康检查构建器。</param>
    /// <param name="tags">宿主标签，例如 ready。</param>
    public static IHealthChecksBuilder AddRabbitMqEventTransport(this IHealthChecksBuilder builder, IEnumerable<string>? tags = null)
    {
        if (!builder.Services.Any(x => x.ServiceType == typeof(HealthRegistration)))
        {
            builder.Services.AddSingleton<HealthRegistration>();
            builder.AddCheck<RabbitMqEventTransportHealthCheck>("event-bus-rabbitmq", tags: ["event-bus"]);
        }
        var additions = tags?.ToArray() ?? [];
        builder.Services.Configure<HealthCheckServiceOptions>(options =>
        {
            var check = options.Registrations.Single(x => x.Name == "event-bus-rabbitmq");
            check.Tags.UnionWith(additions);
        });
        return builder;
    }

    private sealed class HealthRegistration;
}
