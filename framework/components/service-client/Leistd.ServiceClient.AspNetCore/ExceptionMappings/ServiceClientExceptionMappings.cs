using Leistd.ExceptionHandling.AspNetCore.Descriptors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.ServiceClient.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Leistd.ServiceClient.AspNetCore.ExceptionMappings;

/// <summary>服务调用故障在 HTTP 边界的安全默认响应，由宿主显式组合。</summary>
public static class ServiceClientExceptionMappings
{
    // 上游故障是协议层失败：契约就是 502/503/504 这些状态码本身，只带本地化标题与 traceId，
    // 不合成与状态码一一对应的错误码，也不回显上游的 URL 与响应片段（它们只进服务端日志）。
    private static readonly ExceptionDescriptor InternalError = new(StatusCodes.Status500InternalServerError);
    private static readonly ExceptionDescriptor BadGateway = new(StatusCodes.Status502BadGateway);
    private static readonly ExceptionDescriptor Unavailable = new(StatusCodes.Status503ServiceUnavailable);
    private static readonly ExceptionDescriptor Timeout = new(StatusCodes.Status504GatewayTimeout);

    /// <summary>按本地观测到的失败来源登记安全默认响应；宿主可覆盖同一异常类型。</summary>
    /// <remarks>只映射本组件自己的异常类型，不认领 <see cref="TaskCanceledException"/> 这类 BCL 异常：
    /// 那会接管整个应用里所有 HttpClient 与任务的取消，也会与其他组件对同一类型的默认映射相互抢占。</remarks>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultException<ServiceClientException>(exception => exception.FailureKind switch
        {
            ServiceClientFailureKind.InvalidResponse or ServiceClientFailureKind.RemoteFailure => BadGateway,
            ServiceClientFailureKind.Unavailable => Unavailable,
            ServiceClientFailureKind.Timeout => Timeout,
            _ => InternalError
        });
    }
}
