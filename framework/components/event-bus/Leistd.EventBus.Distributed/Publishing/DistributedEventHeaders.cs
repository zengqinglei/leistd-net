namespace Leistd.EventBus.Distributed.Publishing;

/// <summary>跨传输的消息头约定。</summary>
public static class DistributedEventHeaders
{
    /// <summary>W3C 上游 Activity 上下文。</summary>
    public const string TraceParent = "traceparent";

    /// <summary>W3C 链路状态；没有时可省略。</summary>
    public const string TraceState = "tracestate";

    /// <summary>业务关联标识。</summary>
    public const string CorrelationId = "leistd-correlation-id";
}
