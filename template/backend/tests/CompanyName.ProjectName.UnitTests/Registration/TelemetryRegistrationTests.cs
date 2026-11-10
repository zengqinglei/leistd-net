using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        Assert.Single(services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider);
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
        Assert.Single(services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider);
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

    [Fact]
    public void Project_prefix_selects_meters_and_activity_sources()
    {
        var builder = Builder(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null });
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

    // 只组合服务集合，不构建或启动宿主
    /// <summary>
    /// 构建导出管道不得解析宿主生命周期：日志工厂构建期间解析会经日志依赖重入，得到第二个实例，
    /// 停机信号停掉的不是 <c>app.Run()</c> 等待的那个，进程收到 SIGTERM 后不退出。
    /// 用带默认日志与控制台生命周期的宿主核对：容器里的生命周期就是宿主通知启动的那个，发送门槛随之打开。
    /// </summary>
    [Fact]
    public async Task The_host_lifetime_is_a_single_instance_and_opens_the_export_gate()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            Configuration = new ConfigurationManager()
        });
        builder.AddMyProjectTelemetry();
        NoNetwork(builder.Services);
        using var host = builder.Build();
        var gate = host.Services.GetRequiredService<TelemetryExportGate>();
        Assert.False(gate.IsOpen);

        await host.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
            Assert.True(gate.IsOpen);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

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

    // SDK 在释放时也可能冲刷，传输留在内存中以保持单测不发网络请求。
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
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
}
