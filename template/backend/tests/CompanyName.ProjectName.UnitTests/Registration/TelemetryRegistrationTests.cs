using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using CompanyName.ProjectName.Api.Hosting;
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
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t", false)]
    [InlineData("http://localhost:4317", true)]
    public void Endpoint_controls_both_exporters_without_registering_a_log_provider(string? endpoint, bool enabled)
    {
        // 只组合服务集合，不构建或启动宿主。Options 每创建一次对应一个实际导出器。
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
        builder.AddMyProjectTelemetry();
        var exporterCount = 0;
        builder.Services.Configure<OtlpExporterOptions>(options =>
        {
            exporterCount++;
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler());
        });
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<MeterProvider>();
        Assert.Equal(enabled ? 1 : 0, exporterCount);
        services.GetRequiredService<TracerProvider>();
        Assert.Equal(enabled ? 2 : 0, exporterCount);
        Assert.DoesNotContain(services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public void Repeated_registration_keeps_one_exporter_per_signal()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317";
        Assert.Same(builder, builder.AddMyProjectTelemetry());
        var registrationCount = builder.Services.Count;
        builder.AddMyProjectTelemetry();
        Assert.Equal(registrationCount, builder.Services.Count);
        var exporterCount = 0;
        builder.Services.Configure<OtlpExporterOptions>(options =>
        {
            exporterCount++;
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.HttpClientFactory = () => new HttpClient(new NoNetworkHandler());
        });
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<MeterProvider>();
        Assert.Equal(1, exporterCount);
        services.GetRequiredService<TracerProvider>();
        Assert.Equal(2, exporterCount);
    }

    [Fact]
    public void Project_prefix_selects_meters_and_activity_sources()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddMyProjectTelemetry();
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

    // SDK 在释放时也可能冲刷运行时指标，传输留在内存中以保持单测不发网络请求。
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
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
