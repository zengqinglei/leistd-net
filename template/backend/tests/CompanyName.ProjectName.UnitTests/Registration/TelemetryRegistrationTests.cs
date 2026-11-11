using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.UnitTests.Registration;

public class TelemetryRegistrationTests
{
    private const string ProjectAssemblyName = "CompanyName.ProjectName.Api";

    /// <summary>三路导出器与日志提供程序常驻：启用与否只在发送层判断，各只有一个。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("http://localhost:4318")]
    public void Every_signal_has_one_exporter_whether_or_not_export_is_enabled(string? endpoint)
    {
        var builder = Builder(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint });
        builder.AddMyProjectTelemetry();
        var exporters = CountExporters(builder.Services);
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<MeterProvider>();
        services.GetRequiredService<TracerProvider>();
        services.GetRequiredService<LoggerProvider>();
        Assert.Single(services.GetServices<ILoggerProvider>(), provider => provider is TelemetryExportLoggerProvider { Inner: OpenTelemetryLoggerProvider });
        // 选项每创建一次对应一个实际导出器：三路各一个
        Assert.Equal(3, exporters.Count);
    }

    [Fact]
    public void Repeated_registration_keeps_one_exporter_per_signal()
    {
        var builder = Builder(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318" });
        Assert.Same(builder, builder.AddMyProjectTelemetry());
        var registrationCount = builder.Services.Count;
        builder.AddMyProjectTelemetry();
        Assert.Equal(registrationCount, builder.Services.Count);
        var exporters = CountExporters(builder.Services);
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<MeterProvider>();
        services.GetRequiredService<TracerProvider>();
        services.GetRequiredService<LoggerProvider>();
        Assert.Single(services.GetServices<ILoggerProvider>(), provider => provider is TelemetryExportLoggerProvider { Inner: OpenTelemetryLoggerProvider });
        Assert.Equal(3, exporters.Count);
    }

    /// <summary>协议恒为 http/protobuf，SDK 侧不带认证头：部署的协议与认证头环境变量都不改变它。</summary>
    [Fact]
    public void Exporters_use_http_protobuf_without_sdk_headers()
    {
        var builder = Builder(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "x-api-key=deployment"
        });
        builder.AddMyProjectTelemetry();
        var exporters = CountExporters(builder.Services);
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<MeterProvider>();
        services.GetRequiredService<TracerProvider>();
        services.GetRequiredService<LoggerProvider>();

        Assert.Equal(3, exporters.Count);
        Assert.All(exporters, options =>
        {
            Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
            Assert.True(string.IsNullOrEmpty(options.Headers));
        });
    }

    /// <summary>导出器的客户端经发送层：宿主未启动时请求就地丢弃，不发网络。</summary>
    [Fact]
    public async Task Exporter_client_goes_through_the_send_gate()
    {
        var builder = Builder(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:9" });
        builder.AddMyProjectTelemetry();
        Func<HttpClient>? clientFactory = null;
        builder.Services.Configure<OtlpExporterOptions>(options => clientFactory ??= options.HttpClientFactory);
        using var services = builder.Services.BuildServiceProvider();
        services.GetRequiredService<TracerProvider>();

        using var client = Assert.IsType<Func<HttpClient>>(clientFactory)();
        using var response = await client.PostAsync("http://127.0.0.1:9/v1/traces", new ByteArrayContent([1]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>服务名依次取 OTEL_SERVICE_NAME、工作负载身份、程序集名；OTEL_RESOURCE_ATTRIBUTES 里的同名属性不能反过来覆盖。</summary>
    [Theory]
    [InlineData("svc-from-otel", "svc-from-client-id", "svc-from-otel")]
    [InlineData(null, "svc-from-client-id", "svc-from-client-id")]
    [InlineData(" ", " ", ProjectAssemblyName)]
    public void Service_name_is_the_first_configured_source_on_every_signal(string? otelServiceName, string? clientId, string expected)
    {
        var builder = Builder(new()
        {
            ["OTEL_SERVICE_NAME"] = otelServiceName,
            ["Leistd:ServiceAuth:ClientId"] = clientId,
            ["OTEL_RESOURCE_ATTRIBUTES"] = "service.name=from-resource-attributes,deployment.environment=test"
        });
        builder.AddMyProjectTelemetry();
        NoNetwork(builder.Services);
        using var services = builder.Services.BuildServiceProvider();

        BaseProvider[] providers =
        [
            services.GetRequiredService<MeterProvider>(),
            services.GetRequiredService<TracerProvider>(),
            services.GetRequiredService<LoggerProvider>()
        ];
        Assert.All(providers, provider =>
        {
            var attributes = provider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal(expected, attributes["service.name"]);
            // 其余资源属性照常读取：只是 service.name 由入口决定
            Assert.Equal("test", attributes["deployment.environment"]);
        });
    }

    // 导出活动时才采样：配上地址（传输换成内存），追踪按项目前缀选源
    [Fact]
    public void Project_prefix_selects_meters_and_activity_sources()
    {
        var builder = Builder(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318" });
        builder.AddMyProjectTelemetry();
        NoNetwork(builder.Services);
        var metrics = new List<string>();
        var activities = new List<string>();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(provider => provider.AddReader(new PeriodicExportingMetricReader(
                new MetricNameExporter(metrics), exportIntervalMilliseconds: int.MaxValue)))
            .WithTracing(provider => provider.AddProcessor(new SimpleActivityExportProcessor(new ActivityNameExporter(activities))));
        using var services = builder.Services.BuildServiceProvider();
        var meterProvider = services.GetRequiredService<MeterProvider>();
        services.GetRequiredService<TracerProvider>();
        // provider 已启动后才发射；测试不再登记任何 Meter 或 ActivitySource 前缀。
        using var meter = new Meter("CompanyName.ProjectName.Registration");
        using var otherMeter = new Meter("OtherProject.Registration");
        meter.CreateCounter<int>("registration.project").Add(1);
        otherMeter.CreateCounter<int>("registration.other").Add(1);
        using var source = new ActivitySource("CompanyName.ProjectName.Registration");
        using var otherSource = new ActivitySource("OtherProject.Registration");
        using (var activity = source.StartActivity("registration.project"))
        {
            Assert.NotNull(activity);
        }
        Assert.Null(otherSource.StartActivity("registration.other"));
        Assert.True(meterProvider.ForceFlush());

        Assert.Contains("registration.project", metrics);
        Assert.DoesNotContain("registration.other", metrics);
        Assert.Equal(["registration.project"], activities);
    }

    // 导出不活动时追踪由采样器丢弃：span 不记录（不填属性、不进批处理），传播用的上下文照常
    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:4318")]
    public void Spans_are_not_recorded_while_export_is_inactive(string? endpoint)
    {
        var builder = Builder(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            // 有地址但系统设置关了导出，同样不活动
            [TelemetryExportOptions.EnabledKey] = "false",
        });
        builder.AddMyProjectTelemetry();
        NoNetwork(builder.Services);
        var activities = new List<string>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(provider => provider.AddProcessor(new SimpleActivityExportProcessor(new ActivityNameExporter(activities))));
        using var services = builder.Services.BuildServiceProvider();
        services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource("CompanyName.ProjectName.Registration");

        using (var activity = source.StartActivity("registration.inactive"))
        {
            Assert.NotNull(activity);
            Assert.False(activity.Recorded);
            Assert.False(activity.IsAllDataRequested);
        }

        Assert.Empty(activities);
    }

    // 导出不活动时日志不交给 SDK 的提供程序（不排队、不序列化）；活动时照常转交，作用域经外部作用域提供器转给它
    [Fact]
    public void Logs_reach_the_sdk_provider_only_while_export_is_active()
    {
        var export = new TelemetryExportOptions { Enabled = false, Endpoint = "http://collector:4318" };
        var inner = new RecordingProvider();
        using var factory = LoggerFactory.Create(logging => logging
            .Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None)
            .AddProvider(new TelemetryExportLoggerProvider(inner, new FixedMonitor(export))));
        var logger = factory.CreateLogger("Registration");

        logger.LogWarning("while inactive");
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        export.Enabled = true;
        using (logger.BeginScope("registration-scope"))
        {
            logger.LogWarning("while active");
        }

        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.Equal([("while active", "registration-scope")], inner.Entries);
    }

    // 日志仍走官方 AddOtlpExporter：宿主配置源里的属性限额同样生效（自建导出器只读环境变量，会丢掉它）
    [Fact]
    public void Log_attribute_limits_from_host_configuration_still_apply()
    {
        var builder = Builder(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318",
            ["OTEL_LOGRECORD_ATTRIBUTE_VALUE_LENGTH_LIMIT"] = "4",
        });
        builder.AddMyProjectTelemetry();
        var bodies = new List<string>();
        builder.Services.Configure<OtlpExporterOptions>(options =>
            options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler(bodies: bodies)));
        using var services = builder.Services.BuildServiceProvider();

        // 正文不带属性值（入口打开了 IncludeFormattedMessage，正文不受属性限额约束），只核对属性
        KeyValuePair<string, object?>[] state = [new("Payload", "long_attribute_value"), new("{OriginalFormat}", "attribute limit")];
        services.GetRequiredService<ILoggerFactory>().CreateLogger("Registration")
            .Log(LogLevel.Warning, default, state, null, (_, _) => "attribute limit");
        Assert.True(services.GetRequiredService<LoggerProvider>().ForceFlush());

        var body = Assert.Single(bodies);
        Assert.Contains("long", body, StringComparison.Ordinal);
        Assert.DoesNotContain("long_attribute_value", body, StringComparison.Ordinal);
    }

    // 批处理参数取标准配置（OTEL_BLRP_*）：部署调小批次、队列照样生效
    [Fact]
    public void Log_batching_follows_the_standard_processor_settings()
    {
        var builder = Builder(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318",
            ["OTEL_BLRP_MAX_EXPORT_BATCH_SIZE"] = "1",
            ["OTEL_BLRP_SCHEDULE_DELAY"] = "60000",
        });
        builder.AddMyProjectTelemetry();
        var requests = new List<string>();
        builder.Services.Configure<OtlpExporterOptions>(options =>
            options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler(requests)));
        using var services = builder.Services.BuildServiceProvider();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Registration");

        logger.LogWarning("first");
        logger.LogWarning("second");
        logger.LogWarning("third");
        Assert.True(services.GetRequiredService<LoggerProvider>().ForceFlush());

        Assert.Equal(3, requests.Count(uri => uri.EndsWith("/v1/logs", StringComparison.Ordinal)));
    }

    // 部署设 OTEL_SDK_DISABLED=true：什么都不注册（不建采集与导出管道），系统设置不登记导出项
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void A_disabled_sdk_registers_nothing_and_hides_the_export_settings(string disabled)
    {
        var builder = Builder(new()
        {
            [TelemetryExportOptions.SdkDisabledKey] = disabled,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318",
        });
        builder.AddMyProjectTelemetry();
        using var services = builder.Services.BuildServiceProvider();

        Assert.Null(services.GetService<TracerProvider>());
        Assert.Null(services.GetService<MeterProvider>());
        Assert.DoesNotContain(services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider or TelemetryExportLoggerProvider);
        Assert.False(services.GetRequiredService<IOptions<TelemetrySettingsOptions>>().Value.ExportConfigurable);
    }

    // 其他取值与 SDK 一样不算关闭：照常注册，导出项照常登记
    [Theory]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("")]
    public void Other_values_keep_the_sdk_enabled(string value)
    {
        var builder = Builder(new() { [TelemetryExportOptions.SdkDisabledKey] = value });
        builder.AddMyProjectTelemetry();
        NoNetwork(builder.Services);
        using var services = builder.Services.BuildServiceProvider();

        Assert.NotNull(services.GetService<TracerProvider>());
        Assert.True(services.GetRequiredService<IOptions<TelemetrySettingsOptions>>().Value.ExportConfigurable);
    }

    // 只组合服务集合，不构建或启动宿主
    private static HostApplicationBuilder Builder(Dictionary<string, string?> configuration)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        foreach (var (key, value) in configuration)
            builder.Configuration[key] = value;
        return builder;
    }

    // 排在入口的选项配置之后：每创建一个导出器就记下它最终的选项，传输换成内存，单测不发网络
    private static List<OtlpExporterOptions> CountExporters(IServiceCollection services)
    {
        var exporters = new List<OtlpExporterOptions>();
        services.Configure<OtlpExporterOptions>(options =>
        {
            exporters.Add(options);
            options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler());
        });
        return exporters;
    }

    private static void NoNetwork(IServiceCollection services)
        => services.Configure<OtlpExporterOptions>(options => options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler()));

    // SDK 在释放时也可能冲刷，传输留在内存中以保持单测不发网络请求；传入列表时记下请求地址。
    private sealed class NoNetworkHandler(List<string>? requests = null, List<string>? bodies = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requests is not null)
            {
                lock (requests)
                    requests.Add(request.RequestUri!.ToString());
            }

            if (bodies is not null && request.RequestUri!.AbsolutePath.EndsWith("/v1/logs", StringComparison.Ordinal))
            {
                lock (bodies)
                    bodies.Add(System.Text.Encoding.Latin1.GetString(request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()));
            }

            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }

    private sealed class MetricNameExporter(List<string> names) : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch) { names.Add(metric.Name); }
            return ExportResult.Success;
        }
    }

    private sealed class ActivityNameExporter(List<string> names) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch) { names.Add(activity.OperationName); }
            return ExportResult.Success;
        }
    }

    private sealed class RecordingProvider : ILoggerProvider, ISupportExternalScope, ILogger
    {
        private IExternalScopeProvider? scopes;

        public List<(string Message, string? Scope)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string? scope = null;
            scopes?.ForEachScope((value, _) => scope = value?.ToString(), (object?)null);
            Entries.Add((formatter(state, exception), scope));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedMonitor(TelemetryExportOptions value) : IOptionsMonitor<TelemetryExportOptions>
    {
        public TelemetryExportOptions CurrentValue => value;

        public TelemetryExportOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TelemetryExportOptions, string?> listener) => null;
    }
}
