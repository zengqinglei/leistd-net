using Leistd.Tracing.HttpClient.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Tracing.HttpClient;

/// <summary>
/// 关联标识出站透传的注册入口：为 <c>HttpClient</c> 挂上转发处理器。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 为 HttpClient 添加关联标识转发处理器：把当前关联标识（默认即 TraceId，显式指定时为指定值）写入请求头。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddHttpClient("downstream").AddCorrelationIdForwarding();
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddCorrelationIdForwarding(this IHttpClientBuilder builder)
    {
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
        builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        return builder;
    }
}
