using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Metadata;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>路径里含机密（如回调地址中的随机路由键）的端点：请求完成日志与服务端 span 的 <c>url.path</c> 只记路由模板。</summary>
/// <remarks>控制器与动作上直接标注；最小 API 与路由组用 <see cref="SensitiveRouteMetadata.WithSensitiveRoute{TBuilder}"/>。</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SensitiveRouteAttribute : Attribute;

/// <summary>声明与读取 <see cref="SensitiveRouteAttribute"/>。</summary>
/// <remarks>
/// <para>读取方是请求完成日志（<see cref="RequestLoggingInterceptor"/>）与 ASP.NET Core 追踪的富化，两者用同一份路由模板。
/// 每条日志都带的原始路径作用域由 <c>Logging:LogLevel</c> 里关闭 <c>Microsoft.AspNetCore.Hosting.Diagnostics</c> 去掉。</para>
/// <para>只改这两处框架写出的路径；业务日志、异常消息与网关访问日志不要自行记录路径或路由值。</para>
/// </remarks>
public static class SensitiveRouteMetadata
{
    /// <summary>把端点标记为路径含机密。</summary>
    public static TBuilder WithSensitiveRoute<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new SensitiveRouteAttribute());

    /// <summary>本请求命中的端点带标记时返回其路由模板（以 <c>/</c> 开头），否则为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 模板取自追踪 <c>http.route</c> 的同一来源 <see cref="IRouteDiagnosticsMetadata"/>，只补齐开头的 <c>/</c>。
    /// 全局异常处理在重新执行前清掉了端点，这时从 <see cref="IExceptionHandlerFeature"/> 取原端点，4xx 与 5xx 同样只记模板。
    /// </remarks>
    internal static string? SensitiveRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
        if (endpoint?.Metadata.GetMetadata<SensitiveRouteAttribute>() is null)
            return null;

        return "/" + endpoint.Metadata.GetMetadata<IRouteDiagnosticsMetadata>()?.Route.TrimStart('/');
    }
}
