using Leistd.DependencyInjection.Extensions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Kafka.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.Kafka;

/// <summary>Kafka 分布式事件传输注册。</summary>
public static class DependencyInjection
{
    /// <summary>选择 Kafka 传输，登记按事件名订阅的消费者。</summary>
    /// <remarks>消费禁用自动位点存储和提交，持久接收成功后才提交该分区的下一位点；失败保留当前消息重试。默认组名取宿主应用名。</remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">绑定配置后应用的选项。</param>
    /// <param name="configSectionPath">传输配置节。</param>
    /// <example><code>services.AddKafkaEventTransport();</code></example>
    public static IServiceCollection AddKafkaEventTransport(this IServiceCollection services,
        Action<KafkaEventTransportOptions>? configure = null,
        string configSectionPath = KafkaEventTransportOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        services.EnsureSingleAuthoritative<IDistributedEventTransport, KafkaEventTransport>(ServiceLifetime.Singleton,
            "Only one distributed event transport can be registered.");
        var previous = services.Select(x => x.ImplementationInstance).OfType<KafkaEventTransportRegistration>().FirstOrDefault();
        if (previous is not null && previous.Path != configSectionPath) throw new InvalidOperationException("Kafka event transport is already bound to another configuration section.");
        if (previous is null)
        {
            services.AddSingleton(new KafkaEventTransportRegistration(configSectionPath));
            services.AddSingleton<IValidateOptions<KafkaEventTransportOptions>>(sp => new KafkaEventTransportOptionsValidator(
                configSectionPath, sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        }
        var options = services.AddOptions<KafkaEventTransportOptions>().BindConfiguration(configSectionPath);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddSingleton<IDistributedEventTransport, KafkaEventTransport>();
        services.TryAddSingleton(sp => (KafkaEventTransport)sp.GetRequiredService<IDistributedEventTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, KafkaEventTransportWorker>());
        return services;
    }

    /// <summary>登记传输健康检查，附加宿主标签并始终保留 event-bus 标签；重复调用合并标签。</summary>
    /// <param name="builder">健康检查构建器。</param>
    /// <param name="tags">宿主标签，例如 ready。</param>
    public static IHealthChecksBuilder AddKafkaEventTransport(this IHealthChecksBuilder builder, IEnumerable<string>? tags = null)
    {
        if (!builder.Services.Any(x => x.ServiceType == typeof(HealthRegistration)))
        {
            builder.Services.AddSingleton<HealthRegistration>();
            builder.AddCheck<KafkaEventTransportHealthCheck>("event-bus-kafka", tags: ["event-bus"]);
        }
        var additions = tags?.ToArray() ?? [];
        builder.Services.Configure<HealthCheckServiceOptions>(options =>
        {
            var check = options.Registrations.Single(x => x.Name == "event-bus-kafka");
            check.Tags.UnionWith(additions);
        });
        return builder;
    }

    private sealed class HealthRegistration;
}
