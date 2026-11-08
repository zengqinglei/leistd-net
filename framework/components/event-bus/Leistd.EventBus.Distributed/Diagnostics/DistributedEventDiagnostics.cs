using System.Diagnostics;

namespace Leistd.EventBus.Distributed.Diagnostics;

/// <summary>分布式事件发送与处理的活动源。</summary>
public static class DistributedEventDiagnostics
{
    /// <summary>活动源名称。</summary>
    public const string SourceName = "Leistd.EventBus";

    /// <summary>供宿主订阅的活动源；消息头延续发布请求的链路。</summary>
    public static ActivitySource ActivitySource { get; } = new(SourceName);
}
