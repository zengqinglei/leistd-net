namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 远端服务返回的业务错误：非 2xx 响应（ProblemDetails）或 2xx 但统一响应 <c>code != 0</c>。
/// 不映射回本地业务异常——远端 404 不等于本地资源不存在，调用方按需自行翻译。
/// </summary>
public class RemoteServiceException : ServiceClientException
{
    /// <summary>
    /// 远端响应的 HTTP 状态码。
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 远端业务错误码（ProblemDetails 的 <c>code</c> 或统一响应的 <c>code</c>），无法解析时为 <c>null</c>。
    /// </summary>
    public int? ErrorCode { get; }

    /// <summary>
    /// 远端 traceId（ProblemDetails 的 <c>traceId</c>），用于跨服务日志检索；无法解析时为 <c>null</c>。
    /// </summary>
    public string? RemoteTraceId { get; }

    /// <summary>
    /// 远端字段级错误列表（ProblemDetails 的 <c>errors</c>），无则为空数组。
    /// </summary>
    public IReadOnlyList<RemoteErrorItem> Errors { get; }

    /// <summary>
    /// 远端原始响应体（截断），供无法结构化解析时排查。
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// 创建远端服务异常。
    /// </summary>
    public RemoteServiceException(
        string message,
        int statusCode,
        int? errorCode = null,
        string? remoteTraceId = null,
        IReadOnlyList<RemoteErrorItem>? errors = null,
        string? responseBody = null,
        System.Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RemoteTraceId = remoteTraceId;
        Errors = errors ?? [];
        ResponseBody = responseBody;
    }
}
