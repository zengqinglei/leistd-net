using Leistd.Lock.Memory.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Leistd.Lock.Abstractions;

namespace Leistd.Lock.Memory;

/// <summary>
/// 提供进程内锁注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册进程内锁和清理服务，并将其作为单机分布式锁实现。
    /// </summary>
    /// <remarks>
    /// 单副本部署用本方法，多副本部署换成 <c>AddRedisDistributedLock(...)</c>——业务代码统一依赖 <see cref="IDistributedLock"/>，不因部署形态改写。
    /// <b>代价是一条静默降级路径</b>：扩到多副本后互斥当场失效而没有任何报错，
    /// 因此 <see cref="IDistributedLock"/> 首次被解析时打一条 Warning，启动日志里有据可查。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddMemoryLocalLock();   // 单副本部署
    /// </code>
    /// </example>
    public static IServiceCollection AddMemoryLocalLock(this IServiceCollection services)
    {
        services.AddSingleton<MemoryLocalLock>();
        services.AddSingleton<ILocalLock>(sp => sp.GetRequiredService<MemoryLocalLock>());
        services.AddSingleton<ILock>(sp => sp.GetRequiredService<MemoryLocalLock>());

        services.AddSingleton<IDistributedLock>(sp =>
        {
            sp.GetRequiredService<ILogger<MemoryLocalLock>>().LogWarning(
                "IDistributedLock is served by the in-process memory lock. Mutual exclusion holds " +
                "within this process only; it does NOT hold across replicas. Register " +
                "AddRedisDistributedLock(...) before scaling beyond a single instance.");

            return sp.GetRequiredService<MemoryLocalLock>();
        });

        services.AddHostedService<MemoryLockCleanupHostedService>();
        return services;
    }
}
