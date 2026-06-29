using Leistd.EventBus.Core.Event;
using Leistd.EventBus.Core.EventBus;
using Leistd.EventBus.Local.Wrapper;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Leistd.EventBus.Local.EventBus;

/// <summary>
/// 本地事件总线实现（Singleton 生命周期）
/// 支持 Web、Console、BackgroundService 等多种应用场景
/// </summary>
public class LocalEventBus(IServiceScopeFactory serviceScopeFactory) : ILocalEventBus
{
    private static readonly ConcurrentDictionary<Type, EventHandlerWrapper> _wrapperCache = new();

    /// <summary>
    /// 发布事件（泛型重载）。
    /// </summary>
    /// <remarks>
    /// 重要：始终按事件的 <b>运行时类型</b> 路由到具体 <c>IEventHandler&lt;具体事件&gt;</c>，
    /// 而非编译期的 <typeparamref name="TEvent"/>。否则当调用方以基接口（如 ILocalEvent/IEvent）
    /// 的静态类型发布时，会去解析 IEventHandler&lt;基接口&gt; 而漏掉所有具体 handler（静默丢事件）。
    /// 故此重载直接委托给运行时路由的非泛型实现，保证选中哪个重载行为都一致。
    /// </remarks>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
        => PublishAsync((IEvent)@event!, cancellationToken);

    /// <summary>
    /// 发布事件（非泛型）。按事件运行时类型解析并调用对应的 <c>IEventHandler&lt;T&gt;</c>。
    /// </summary>
    [OverloadResolutionPriority(1)]
    public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        var eventType = @event.GetType();

        var wrapper = _wrapperCache.GetOrAdd(eventType, t =>
        {
            var wrapperType = typeof(EventHandlerWrapperImpl<>).MakeGenericType(t);
            return (EventHandlerWrapper)Activator.CreateInstance(wrapperType)!;
        });

        // 委托给包装器执行，恢复泛型上下文（运行时类型）
        return wrapper.HandleAsync(@event, serviceScopeFactory, cancellationToken);
    }
}
