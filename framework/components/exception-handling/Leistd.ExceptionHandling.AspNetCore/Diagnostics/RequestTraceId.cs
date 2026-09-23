using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Leistd.ExceptionHandling.AspNetCore.Diagnostics;

/// <summary>读取请求入口确定的链路标识；请求未提供标识时回落到 Activity。</summary>
public static class RequestTraceId
{
    /// <summary>返回对外错误响应与日志共用的 Trace ID。</summary>
    public static string Get(HttpContext context)
    {
        var identifier = context.TraceIdentifier;
        return !string.IsNullOrEmpty(identifier)
            ? identifier
            : Activity.Current?.TraceId.ToHexString() ?? string.Empty;
    }
}
