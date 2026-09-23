using Leistd.ExceptionHandling;
namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 表示远端服务明确返回的失败响应。
/// </summary>
/// <remarks>
/// 不自动映射为本地业务异常；宿主可在 API 边界按自身契约处理。
/// </remarks>
public class RemoteServiceException : ServiceClientException
{
    /// <summary>
    /// 远端响应的 HTTP 状态码。
    /// </summary>
    /// <remarks>这是"我们发给远端那次请求"得到的状态码；通用异常映射不会据此决定本地状态码，宿主可按已知远端契约显式处理。</remarks>
    public int RemoteStatusCode { get; }

    /// <summary>
    /// 远端业务错误码（ProblemDetails 的 <c>code</c> 或数字信封的 <c>errorCode</c>），无法解析时为 <c>null</c>。
    /// </summary>
    /// <remarks>仍兼容旧信封中仅有数字 <c>code</c> 的形状，取到时按不变文化转成字符串。</remarks>
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
        : base(message, innerException, ServiceClientFailureKind.RemoteFailure)
    {
        RemoteStatusCode = statusCode;
        ErrorCode = errorCode;
        RemoteTraceId = remoteTraceId;
        Errors = errors ?? [];
        ResponseBody = responseBody;
    }
}
