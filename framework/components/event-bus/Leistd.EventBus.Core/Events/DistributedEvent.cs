namespace Leistd.EventBus.Events;

/// <summary>分布式事件基类；JSON 反序列化通过继承的 init 属性还原标识与发生时刻。</summary>
public abstract class DistributedEvent : BaseEvent, IDistributedEvent
{
    /// <summary>使用系统当前 UTC 时刻创建事件。</summary>
    protected DistributedEvent()
    {
    }

    /// <summary>使用发布方的 UTC 时刻创建事件。</summary>
    protected DistributedEvent(DateTime occurredOn) : base(occurredOn)
    {
    }
}
