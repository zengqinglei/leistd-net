namespace Leistd.EventBus.Events;

/// <summary>可跨进程持久投递的事件；标识与发生时刻在接收和重放时保持不变。</summary>
public interface IDistributedEvent : IEvent
{
}
