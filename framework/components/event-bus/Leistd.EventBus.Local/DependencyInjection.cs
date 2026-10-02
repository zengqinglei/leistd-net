using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.EventBus.Abstractions;

namespace Leistd.EventBus.Local;

/// <summary>
/// 进程内事件总线的注册入口：注册 <c>ILocalEventBus</c> 与按约定发现的 <c>IEventHandler</c>。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册单例进程内事件总线。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddLocalEventBus();
    ///
    /// // 处理器按 IEventHandler&lt;TEvent&gt; 注册，发布方只依赖 ILocalEventBus
    /// builder.Services.AddScoped&lt;IEventHandler&lt;OrderPlaced&gt;, OrderPlacedNotifier&gt;();
    /// </code>
    /// </example>
    /// <remarks>可重复调用：DDD 基座等组件会替宿主调用一次，宿主再调用不会重复注册。</remarks>
    public static IServiceCollection AddLocalEventBus(this IServiceCollection services)
    {
        services.TryAddSingleton<LocalEventBus>();
        services.TryAddSingleton<ILocalEventBus>(sp => sp.GetRequiredService<LocalEventBus>());
        // IEventBus 只作各类总线的共同基接口，不注册为服务：同一接口由多种总线注册时，
        // 注入 IEventBus 的发布方会静默换成另一种投递语义（不再等到提交后、需要序列化与 Outbox），编译与测试都发现不了。
        // 与 ILock 同一处理。发布方注入 ILocalEventBus。
        // 调度器必须复用同一实例，才能排空该实例上的待发布事件。
        services.TryAddSingleton<ILocalEventDispatcher>(sp => sp.GetRequiredService<LocalEventBus>());
        return services;
    }
}
