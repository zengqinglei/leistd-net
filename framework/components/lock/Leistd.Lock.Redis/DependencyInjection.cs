using Microsoft.Extensions.Configuration;
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
    /// 注册 Redis 分布式锁并绑定配置节。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">Redis 连接串</param>
    /// <param name="configuration">应用配置</param>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisDistributedLock(options =&gt;
    /// {
    ///     options.ConnectionString = "localhost:6379";
    ///     options.KeyPrefix = "acme-shop:prod:";   // 多应用共用 Redis 时必须设置
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.Configure<RedisLockOptions>(configuration.GetSection(RedisLockOptions.SectionName));
        return services.AddRedisDistributedLockCore(connectionString);
    }

    /// <summary>
    /// 使用委托配置注册 Redis 分布式锁。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">Redis 连接串</param>
    /// <param name="configure">选项配置委托</param>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddRedisDistributedLockCore(connectionString);
    }

    private static IServiceCollection AddRedisDistributedLockCore(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // 租约与重试间隔配错都是静默的生产事故（忙轮询 / 互斥当场不成立），必须在接流量之前失败
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisLockOptions>, RedisLockOptionsValidator>());
        services.AddOptions<RedisLockOptions>().ValidateOnStart();

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));

        // 实现类型只注册一次，其余接口作为别名转发，确保共享连接、指标和续期状态。
        services.TryAddSingleton<RedisDistributedLock>();
        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<RedisDistributedLock>());
        services.TryAddSingleton<ILock>(sp => sp.GetRequiredService<RedisDistributedLock>());

        return services;
    }
}
