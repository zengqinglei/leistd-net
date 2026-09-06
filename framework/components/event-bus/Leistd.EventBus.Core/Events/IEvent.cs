namespace Leistd.EventBus.Events;

/// <summary>
/// 定义可追踪的事件元数据。
/// </summary>
public interface IEvent
{
    /// <summary>
    /// 获取用于幂等和追踪的事件标识。
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// 获取事件的发生时间。
    /// </summary>
    DateTime OccurredOn { get; }
}
