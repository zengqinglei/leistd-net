namespace Leistd.EventBus.Events;

/// <summary>
/// 表示进程内发布的事件基类。
/// </summary>
public abstract class LocalEvent : BaseEvent, ILocalEvent
{
}
