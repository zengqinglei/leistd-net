namespace Leistd.EventBus.Events;

/// <summary>声明需要按键串行发送与处理的分布式事件。</summary>
public interface IOrderedDistributedEvent : IDistributedEvent
{
    /// <summary>排序键；同租户、同事件名、同键有序，死信或处理失败不阻塞后续。</summary>
    string OrderingKey { get; }
}
