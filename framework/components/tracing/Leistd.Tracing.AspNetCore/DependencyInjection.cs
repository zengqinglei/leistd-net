using Leistd.Tracing.AspNetCore.Middlewares;
using Leistd.Tracing.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Tracing.AspNetCore;

/// <summary>
/// 链路追踪的 ASP.NET Core 接入入口：入站中间件与配置绑定。
/// </summary>
public static class DependencyInjection
{
    /// <summary>注册链路追踪（含核心能力），选项绑定自配置节。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">承载 <c>CorrelationIdOptions</c> 配置节的配置根。</param>
    /// <example>
    /// <code>
    /// builder.Services.AddCorrelationId(builder.Configuration);
    ///
    /// app.UseCorrelationId();   // 尽量靠近管道前端，使后续中间件的日志都带上链路标识
    /// </code>
    /// </example>
    public static IServiceCollection AddCorrelationId(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCorrelationIdCore(configuration);
        return services;
    }

    /// <summary>注册链路追踪（含核心能力），选项以委托配置。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">选项配置委托。</param>
    public static IServiceCollection AddCorrelationId(
        this IServiceCollection services,
        Action<CorrelationIdOptions> configureOptions)
    {
        services.AddCorrelationIdCore(configureOptions);
        return services;
    }

    /// <summary>把入站链路标识中间件接入请求管道。应尽量靠近管道前端。</summary>
    /// <param name="app">应用构建器。</param>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}