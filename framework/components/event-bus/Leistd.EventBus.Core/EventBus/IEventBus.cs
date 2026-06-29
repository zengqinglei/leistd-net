using System.Runtime.CompilerServices;
using Leistd.EventBus.Core.Event;

namespace Leistd.EventBus.Core.EventBus;

/// <summary>
/// 事件总线接口
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 发布事件（泛型）。实现按事件运行时类型路由，故与非泛型重载行为一致。
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// 发布事件（非泛型，按运行时类型路由）。
    /// </summary>
    /// <remarks>
    /// 标记更高重载优先级：当以基接口（IEvent/ILocalEvent）静态类型发布时，
    /// 编译器优先选中此重载，避免误选泛型重载导致按编译期类型解析 handler。
    /// </remarks>
    [OverloadResolutionPriority(1)]
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);
}
