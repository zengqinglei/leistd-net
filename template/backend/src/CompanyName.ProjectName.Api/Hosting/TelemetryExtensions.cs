using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Options;
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

    /// <summary>注册三路信号的采集与 OTLP 导出；是否导出、发到哪由 <see cref="TelemetryExportOptions"/> 在运行时决定。</summary>
    /// <remarks>
    /// <para>部署设 <c>OTEL_SDK_DISABLED=true</c> 时什么都不注册（SDK 只在启动时读这个开关），系统设置里也不出现导出项。
    /// 测试宿主默认这样关闭，遥测自己的用例再打开。</para>
    /// <para>否则三路导出管道常驻：OpenTelemetry .NET 不支持运行期增删导出器，而导出开关可在系统设置里改。
    /// 导出不活动时尽早丢弃：追踪由采样器丢弃，不记录；日志不交给 SDK 的日志提供程序（见 <see cref="TelemetryExportLoggerProvider"/>）；
    /// 指标照常在内存里聚合，在发送层丢弃。</para>
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
        if (TelemetryExportOptions.IsSdkDisabled(builder.Configuration))
        {
            builder.Services.Configure<TelemetrySettingsOptions>(options => options.ExportConfigurable = false);
            return builder;
        }

        builder.Services.AddSingleton<TelemetryExportGate>();
        builder.Services.AddSingleton<TelemetryExportFailureReporter>();
        builder.Services.AddSingleton<TelemetryExportSampler>();
        builder.Services.AddHostedService<TelemetryExportGateOpener>();
        AddExportOptions(builder.Services, TelemetryExportOptions.IsEnabledByDeployment(builder.Configuration));
        // 逐信号的导出器都用默认名的选项：这里的配置排在 SDK 读取环境变量之后，协议不会被 OTEL_EXPORTER_OTLP_PROTOCOL 改回
        builder.Services.AddOptions<OtlpExporterOptions>()
            .Configure<IOptionsMonitor<TelemetryExportOptions>, TelemetryExportGate, TelemetryExportFailureReporter>((options, export, gate, failures) =>
            {
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Headers = null;
                var timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);
                // 与 SDK 默认客户端一致不跟随重定向：跟随会把 POST 改成 GET，落到登录页的 200 被当成导出成功
                options.HttpClientFactory = () => new HttpClient(new TelemetryExportHandler(export, gate, failures)
                {
                    InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false }
                })
                {
                    Timeout = timeout
                };
            });

        var loggerProvidersBefore = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider)).ToHashSet();
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
            .WithTracing(tracing => tracing.SetSampler<TelemetryExportSampler>()
                .AddSource(ProjectSourcePattern)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/api/health");
                    // 响应阶段端点已确定，且在检测写完 url.path 之后执行：路径含机密的端点只记路由模板。
                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        if (SensitiveRouteMetadata.SensitiveRouteTemplate(response.HttpContext) is { } route)
                            activity.SetTag(UrlPathTag, response.HttpContext.Request.PathBase.Value + route);
                    };
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());
        GateOpenTelemetryLoggerProvider(builder.Services, loggerProvidersBefore);

        return builder;
    }

    // SDK 的日志提供程序换成包了门控的同一实例（见 TelemetryExportLoggerProvider）：按注册前后的差找出 SDK 新加的那一项
    private static void GateOpenTelemetryLoggerProvider(IServiceCollection services, HashSet<ServiceDescriptor> before)
    {
        var sdkProvider = services.Single(descriptor => descriptor.ServiceType == typeof(ILoggerProvider) && !before.Contains(descriptor));
        services.Remove(sdkProvider);
        services.AddSingleton<ILoggerProvider>(serviceProvider => new TelemetryExportLoggerProvider(
            (ILoggerProvider)(sdkProvider.ImplementationInstance
                ?? sdkProvider.ImplementationFactory?.Invoke(serviceProvider)
                ?? ActivatorUtilities.CreateInstance(serviceProvider, sdkProvider.ImplementationType!)),
            serviceProvider.GetRequiredService<IOptionsMonitor<TelemetryExportOptions>>()));
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
