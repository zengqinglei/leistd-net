using CompanyName.ProjectName.Api.Options;
using Leistd.Tracing.AspNetCore;
using Microsoft.AspNetCore.HttpLogging;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>请求完成日志与链路标识的注册入口。</summary>
/// <remarks>
/// 日志提供程序沿用宿主默认（控制台等），级别与格式全部来自 <c>Logging</c> 配置节，随配置重载即时生效；
/// OTLP 日志导出由 <see cref="TelemetryExtensions"/> 注册。
/// </remarks>
public static class LoggingExtensions
{
    /// <summary>注册请求完成日志、其开关选项与 CorrelationId。</summary>
    public static WebApplicationBuilder AddMyProjectLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<RequestLoggingOptions>()
            .Bind(builder.Configuration.GetSection(RequestLoggingOptions.SectionName));

        // 每个请求一条合并日志，只取最小字段：请求头、正文与查询串可能含个人信息，不记录
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });
        builder.Services.AddHttpLoggingInterceptor<RequestLoggingInterceptor>();

        builder.Services.AddCorrelationId();

        return builder;
    }
}
