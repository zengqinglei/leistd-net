using Microsoft.Extensions.DependencyInjection;
using Leistd.EventBus.Abstractions;

namespace Leistd.EventBus.Local;

/// <summary>
/// 进程内事件总线的注册入口：注册 <c>IEventBus</c> / <c>ILocalEventBus</c> 与按约定发现的 <c>IEventHandler</c>。
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
    public static IServiceCollection AddLocalEventBus(this IServiceCollection services)
    {
        services.AddSingleton<LocalEventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<LocalEventBus>());
        services.AddSingleton<ILocalEventBus>(sp => sp.GetRequiredService<LocalEventBus>());
        // 调度器必须复用同一实例，才能排空该实例上的待发布事件。
        services.AddSingleton<ILocalEventDispatcher>(sp => sp.GetRequiredService<LocalEventBus>());
        return services;
    }
}
