using Leistd.ExceptionHandling;
namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 表示远端服务返回的业务错误。
/// </summary>
/// <remarks>
/// 不自动映射为本地业务异常。远端 5xx、408 和 429 对外映射为 503，其余错误映射为 502。
/// </remarks>
public class RemoteServiceException : ServiceClientException
{
    /// <summary>
    /// 远端响应的 HTTP 状态码。
    /// </summary>
    /// <remarks>
    /// 与继承来的 <see cref="BusinessException.StatusCode"/>（本服务<b>对外</b>的状态码，502/503）是两回事：
    /// 这里是"我们发给远端那次请求"得到的状态码，只用于日志与排查，不透传给调用方。
    /// </remarks>
    public int RemoteStatusCode { get; }

    /// <summary>
    /// 远端业务错误码（ProblemDetails 的 <c>code</c> 或统一响应的 <c>code</c>），无法解析时为 <c>null</c>。
    /// </summary>
    /// <remarks>统一响应信封的 <c>code</c> 是数字，取到时按不变文化转成字符串，与 ProblemDetails 的字符串码同一字段承载。</remarks>
    public string? ErrorCode { get; }

    /// <summary>
    /// 远端 traceId（ProblemDetails 的 <c>traceId</c>），用于跨服务日志检索；无法解析时为 <c>null</c>。
    /// </summary>
    public string? RemoteTraceId { get; }

    /// <summary>
    /// 远端字段级错误列表（Problem Details 的 <c>errors</c>，或统一响应信封的 <c>errors</c>），无则为空数组。
    /// </summary>
    public IReadOnlyList<ErrorItem> Errors { get; }

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
        string? errorCode = null,
        string? remoteTraceId = null,
        IReadOnlyList<ErrorItem>? errors = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(ResolveStatusCode(statusCode), message, innerException)
    {
        RemoteStatusCode = statusCode;
        ErrorCode = errorCode;
        RemoteTraceId = remoteTraceId;
        Errors = errors ?? [];
        ResponseBody = responseBody;
    }

    private static int ResolveStatusCode(int remoteStatusCode) =>
        remoteStatusCode is 408 or 429 or >= 500 ? 503 : 502;
}
