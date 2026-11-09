using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>指标与追踪的宿主注册入口。</summary>
public static class TelemetryExtensions
{
    private const string ProjectSourcePattern = "CompanyName.ProjectName.*";

    /// <summary>注册指标与追踪采集；配置 OTLP 端点时启用两者的导出。</summary>
    public static TBuilder AddMyProjectTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(TelemetryRegistrationMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<TelemetryRegistrationMarker>();
        var exportEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(ProjectSourcePattern);

                if (exportEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(ProjectSourcePattern)
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/api/health"))
                    .AddHttpClientInstrumentation();

                if (exportEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }

    private sealed class TelemetryRegistrationMarker;
}
