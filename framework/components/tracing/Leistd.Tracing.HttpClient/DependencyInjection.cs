using Leistd.Tracing.HttpClient.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Tracing.HttpClient;

/// <summary>
/// 链路标识出站透传的注册入口：为 <c>HttpClient</c> 挂上转发处理器。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 为 HttpClient 添加 TraceId 转发处理器。
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
