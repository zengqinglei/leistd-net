namespace Leistd.EventBus.Distributed.Options;

/// <summary>内置测试事件配置。</summary>
public sealed class DistributedEventTestOptions
{
    /// <summary>是否登记与允许发布 event-bus.test，默认开启。</summary>
    public bool Enabled { get; set; } = true;
}
