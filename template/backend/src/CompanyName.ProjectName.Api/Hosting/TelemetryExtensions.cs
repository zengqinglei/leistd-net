using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>日志、指标与追踪的宿主注册入口。</summary>
public static class TelemetryExtensions
{
    private const string ProjectSourcePattern = "CompanyName.ProjectName.*";
    private const string UrlPathTag = "url.path";

    /// <summary>注册三路信号的采集与 OTLP 导出；是否发送、发到哪由 <see cref="TelemetryExportOptions"/> 在发送时决定。</summary>
    /// <remarks>
    /// <para>三路导出器无论是否启用都登记：OpenTelemetry .NET 不支持运行期增删导出器，而导出开关可在系统设置里改。
    /// 未启用时采集与序列化照常进行，只在发送层丢弃。</para>
    /// <para>协议固定 <c>http/protobuf</c>：发送层改写的是 HTTP 请求，地址与认证头只由它写入。</para>
    /// </remarks>
    public static TBuilder AddMyProjectTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(TelemetryRegistrationMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<TelemetryRegistrationMarker>();
        builder.Services.AddSingleton<TelemetryExportGate>();
        builder.Services.AddHostedService<TelemetryExportGateOpener>();
        AddExportOptions(builder.Services, TelemetryExportOptions.IsEnabledByDeployment(builder.Configuration));
        // 逐信号的导出器都用默认名的选项：这里的配置排在 SDK 读取环境变量之后，协议不会被 OTEL_EXPORTER_OTLP_PROTOCOL 改回
        builder.Services.AddOptions<OtlpExporterOptions>()
            .Configure<IOptionsMonitor<TelemetryExportOptions>, TelemetryExportGate>((options, export, gate) =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Headers = null;
                var timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);
                // 与 SDK 默认客户端一致不跟随重定向：跟随会把 POST 改成 GET，落到登录页的 200 被当成导出成功
                options.HttpClientFactory = () => new HttpClient(new TelemetryExportHandler(export, gate)
                {
                    InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false }
                })
                {
                    Timeout = timeout
                };
            });

        var assemblyName = typeof(TelemetryExtensions).Assembly.GetName();
        var serviceName = ServiceName(builder.Configuration, assemblyName.Name!);
        builder.Services.AddOpenTelemetry()
            // 放在资源配置末尾，且其后不再追加检测器：OTEL_RESOURCE_ATTRIBUTES 里的 service.name 不会反过来覆盖它
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: assemblyName.Version?.ToString()))
            .WithLogging(
                logging => logging.AddOtlpExporter(),
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                })
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ProjectSourcePattern)
                .AddOtlpExporter())
            .WithTracing(tracing => tracing.AddSource(ProjectSourcePattern)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/api/health");
                    // 响应阶段端点已确定，且在检测写完 url.path 之后执行：路径含机密的端点只记路由模板
                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        if (SensitiveRouteMetadata.SensitiveRouteTemplate(response.HttpContext) is { } route)
                            activity.SetTag(UrlPathTag, response.HttpContext.Request.PathBase.Value + route);
                    };
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return builder;
    }

    // 服务名依次取 OTEL_SERVICE_NAME、工作负载身份 Leistd:ServiceAuth:ClientId、程序集名（与 service-info 一致）
    private static string ServiceName(IConfiguration configuration, string assemblyName)
        => new[] { configuration["OTEL_SERVICE_NAME"], configuration["Leistd:ServiceAuth:ClientId"] }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? assemblyName;

    // 三个键随宿主设置配置源的重载更新；开关缺键时取注册期算出的部署基线，不按已含系统覆盖的有效配置重算
    private static void AddExportOptions(IServiceCollection services, bool enabledByDeployment)
    {
        services.AddOptions<TelemetryExportOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.Enabled = bool.TryParse(configuration[TelemetryExportOptions.EnabledKey], out var enabled)
                ? enabled
                : enabledByDeployment;
            options.Endpoint = configuration[TelemetryExportOptions.EndpointKey];
            options.Headers = configuration[TelemetryExportOptions.HeadersKey];
        });
        services.AddSingleton<IOptionsChangeTokenSource<TelemetryExportOptions>>(serviceProvider =>
            new ConfigurationChangeTokenSource<TelemetryExportOptions>(serviceProvider.GetRequiredService<IConfiguration>()));
    }

    private sealed class TelemetryRegistrationMarker;

    // 宿主启动时才解析生命周期，此时依赖注入已就绪，取到的是 app.Run() 等待的同一个实例
    private sealed class TelemetryExportGateOpener(IHostApplicationLifetime lifetime, TelemetryExportGate gate) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.ApplicationStarted.Register(gate.Open);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
