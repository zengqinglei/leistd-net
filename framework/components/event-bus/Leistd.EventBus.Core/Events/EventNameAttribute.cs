namespace Leistd.EventBus.Events;

/// <summary>指定跨服务事件名称；未标注时取事件类型全名。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventNameAttribute : Attribute
{
    /// <summary>跨服务稳定的事件名称。</summary>
    public string Name { get; }

    /// <summary>指定事件名称；不允许空白。</summary>
    public EventNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
