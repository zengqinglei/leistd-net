using Leistd.Lock.Registration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Leistd.Lock.Redis.Options;
using Leistd.Lock.Abstractions;

namespace Leistd.Lock.Redis;

/// <summary>
/// 提供 Redis 分布式锁注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Redis 分布式锁：绑定配置节，再应用宿主的编程式配置（代码覆盖配置文件）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">Redis 连接串</param>
    /// <param name="configure">编程式配置，在配置节绑定之后应用</param>
    /// <param name="configSectionPath">配置节路径，默认 <c>Leistd:Lock:Redis</c></param>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisDistributedLock(
    ///     builder.Configuration.GetConnectionString("Redis")!,
    ///     options =&gt; options.KeyPrefix = "acme-shop:prod:");   // 多应用共用 Redis 时必须设置
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure = null,
        string configSectionPath = RedisLockOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // 租约与重试间隔配错都是静默的生产事故（忙轮询 / 互斥当场不成立），必须在接流量之前失败
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisLockOptions>>(new RedisLockOptionsValidator(configSectionPath)));
        var options = services.AddOptions<RedisLockOptions>().BindConfiguration(configSectionPath).ValidateOnStart();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));

        // 内存锁的兜底让位给真正的分布式实现（与注册顺序无关）；只移除兜底那一条，宿主自己注册的实现不覆盖
        if (services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(InProcessDistributedLockMarker))
                ?.ImplementationInstance is InProcessDistributedLockMarker marker)
        {
            services.Remove(marker.Fallback);
            services.RemoveAll<InProcessDistributedLockMarker>();
        }

        services.TryAddSingleton<RedisDistributedLock>();
        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<RedisDistributedLock>());

        return services;
    }
}
