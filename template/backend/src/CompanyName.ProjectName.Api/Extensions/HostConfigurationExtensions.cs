using CompanyName.ProjectName.Api.Logging;
using CompanyName.ProjectName.Application.Settings.Hosting;
using Leistd.Tracing.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

namespace CompanyName.ProjectName.Api.Extensions;

public static class HostConfigurationExtensions
{
    /// <summary>
    /// 配置 Web 服务器选项 (Kestrel, IIS, Form)
    /// </summary>
    public static IServiceCollection AddMyProjectWebServer(this IServiceCollection services)
    {
        // 限制请求体大小为 500MB（针对 Gemini 1.5 Pro 视频上传优化，防止 OOM 但允许大文件流式传输）
        const long MaxRequestBodySize = 524288000; // 500MB

        services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = MaxRequestBodySize;
        });

        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = MaxRequestBodySize;
        });

        services.Configure<FormOptions>(options =>
        {
            options.ValueLengthLimit = int.MaxValue;
            options.MultipartBodyLengthLimit = MaxRequestBodySize;
            options.MultipartHeadersLengthLimit = int.MaxValue;
        });

        return services;
    }

    /// <summary>
    /// 配置基础设施服务 (Serilog, CorrelationId, HttpClient Defaults)
    /// </summary>
    public static WebApplicationBuilder AddMyProjectInfrastructure(this WebApplicationBuilder builder)
    {
        // 日志级别走一个进程内的开关：Serilog 的 MinimumLevel.ControlledBy 是它给运行期
        // 改级别的正规入口，改开关立即生效，不必重建 logger 也不必让配置源支持重载。
        // 开关是单例，在这里就地建好并同时交给 Serilog 与 DI——两边必须是同一个实例，
        // 各建一个的话，界面改完的是没接到 logger 上的那一个。
        // 基线与设置定义的默认值同源：清除库里的覆盖值就回到部署配置里那个级别。
        var loggingState = new LoggingSettingState(LoggingBaseline.From(builder.Configuration));
        builder.Services.AddSingleton(loggingState);
        // 状态是单例（进程内共享的那份），应用器是 Scoped：它要读 ISettingProvider，
        // 而后者按请求解析并记忆化。注册成单例会在启动期被作用域校验直接拒掉。
        builder.Services.AddScoped<IHostSettingApplier, LoggingSettingApplier>();
        builder.Services.AddOptions<HostSettingRefreshOptions>()
            .Bind(builder.Configuration.GetSection(HostSettingRefreshOptions.SectionName));
        builder.Services.AddHostedService<HostSettingRefresher>();

        builder.Services.AddSerilog((services, lc) =>
        {
            lc.ReadFrom.Configuration(builder.Configuration)
              .MinimumLevel.ControlledBy(loggingState.MinimumLevel)
              .Enrich.FromLogContext();
        });

        // CorrelationId
        builder.Services.AddCorrelationId(builder.Configuration);



        return builder;
    }
}
