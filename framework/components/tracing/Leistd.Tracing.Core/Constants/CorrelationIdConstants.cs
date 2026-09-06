namespace Leistd.Tracing.Constants;

/// <summary>
/// 链路标识在日志上下文中使用的键名常量。
/// </summary>
public static class CorrelationIdConstants
{
    /// <summary>
    /// 日志上下文中的 TraceId 键名。
    /// </summary>
    /// <remarks>
    /// 该键名是跨语言链路检索的约定键：同一套系统里各服务无论用什么技术栈实现，
    /// 都必须写同一个键，运维才能用一次查询命中全链路。改它等于让既有日志检索失效。
    /// </remarks>
    public const string TraceIdLogKey = "leistd.correlationId.traceId";

    /// <summary>
    /// 获取被 <see cref="System.Diagnostics.Activity"/> 取代的入站链路标识键名。
    /// </summary>
    /// <remarks>
    /// 仅在入站值与 Activity TraceId 不同时写入。
    /// </remarks>
    public const string InboundTraceIdLogKey = "leistd.correlationId.inboundTraceId";
}
