using System.Data.Common;
using System.Diagnostics;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Errors;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.ExceptionHandling;
using Leistd.Settings.Definitions;
using Leistd.Settings.Management;
using Leistd.UnitOfWork;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>遥测导出设置：从进程启动起遵守已保存的值，地址与认证头随设置即时切换，继承值与实际发送一致。</summary>
/// <remarks>
/// 接收端是本机一个真实的 HTTP 监听器，三路信号经 SDK 的真实导出器与发送层到达；
/// 每次核对前主动冲刷三个提供程序，不等批处理周期。
/// </remarks>
public sealed class TelemetryExportTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private static readonly ActivitySource Source = new("CompanyName.ProjectName.TelemetryExportTests");

    /// <summary>只配部署地址、库里没有覆盖：设置的继承值与发送层一致；写入覆盖再清除后仍一致。</summary>
    [Fact]
    public async Task Deployment_endpoint_alone_enables_export_and_matches_the_inherited_setting_values()
    {
        using var receiver = new OtlpReceiver();
        using var host = Host(new()
        {
            [TelemetryExportOptions.EndpointKey] = receiver.BaseAddress,
            [TelemetryExportOptions.HeadersKey] = "x-api-key=deployment"
        });
        await ClearAsync(host);
        var definitions = host.Services.GetRequiredService<ISettingDefinitionManager>();
        var options = host.Services.GetRequiredService<IOptionsMonitor<TelemetryExportOptions>>();

        Assert.Equal("true", definitions.GetOrNull(SettingConstant.Telemetry.ExportEnabled)!.DefaultValue);
        Assert.Equal(receiver.BaseAddress, definitions.GetOrNull(SettingConstant.Telemetry.ExportEndpoint)!.DefaultValue);
        // 机密设置不下发默认值，部署的认证头只在接收端核对
        Assert.Null(definitions.GetOrNull(SettingConstant.Telemetry.ExportHeaders)!.DefaultValue);
        Assert.True(options.CurrentValue.Enabled);
        Assert.Equal(receiver.BaseAddress, options.CurrentValue.Endpoint);

        AssertAllSignals(Flush(host, receiver), "x-api-key", "deployment");

        await SetAsync(host, SettingConstant.Telemetry.ExportEnabled, "false");
        Assert.False(options.CurrentValue.Enabled);
        Assert.Empty(Flush(host, receiver));

        await SetAsync(host, SettingConstant.Telemetry.ExportEnabled, null);
        Assert.True(options.CurrentValue.Enabled);
        AssertAllSignals(Flush(host, receiver), "x-api-key", "deployment");
    }

    /// <summary>部署已启用、库里已保存关闭：启动期间的日志与主动冲刷都不发，启动后照样遵守库里的值。</summary>
    /// <remarks>首次读取设置表时（宿主启动阶段，设置尚未加载）在原地写日志、开追踪并冲刷三路，证明就绪前不发送。</remarks>
    [Fact]
    public async Task A_saved_disabled_setting_is_honoured_from_process_start()
    {
        using var receiver = new OtlpReceiver();
        await SetAsync(factory, SettingConstant.Telemetry.ExportEnabled, "false");
        try
        {
            var probe = new StartupFlushProbe(receiver);
            using var host = Host(new() { [TelemetryExportOptions.EndpointKey] = receiver.BaseAddress }, probe);
            _ = host.Services;

            Assert.True(probe.Flushed, "The first settings read was never intercepted; the startup window was not exercised");
            Assert.False(probe.HostStartedAtFlush);
            Assert.Equal(0, probe.ReceivedDuringStartup);
            Assert.Empty(receiver.Requests);
            Assert.False(host.Services.GetRequiredService<IOptionsMonitor<TelemetryExportOptions>>().CurrentValue.Enabled);
            Assert.Empty(Flush(host, receiver));

            // 接收端确实可达：同一宿主打开后三路随即到达
            await SetAsync(host, SettingConstant.Telemetry.ExportEnabled, "true");
            AssertAllSignals(Flush(host, receiver));
        }
        finally
        {
            await SetAsync(factory, SettingConstant.Telemetry.ExportEnabled, null);
        }
    }

    /// <summary>公共、按信号的部署值与系统覆盖并存：协议固定 http/protobuf，地址与认证头取系统覆盖，清除回到部署配置。</summary>
    [Fact]
    public async Task System_endpoint_and_headers_replace_deployment_values_and_clearing_restores_them()
    {
        using var deployment = new OtlpReceiver();
        using var system = new OtlpReceiver();
        using var host = Host(new()
        {
            [TelemetryExportOptions.EndpointKey] = deployment.BaseAddress,
            [TelemetryExportOptions.HeadersKey] = "x-api-key=deploy-a,x-tenant=deploy-b",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = "http://127.0.0.1:9/traces",
            ["OTEL_EXPORTER_OTLP_TRACES_HEADERS"] = "x-api-key=per-signal"
        });
        await ClearAsync(host);
        try
        {
            await SetAsync(host, SettingConstant.Telemetry.ExportEndpoint, system.BaseAddress);
            await SetAsync(host, SettingConstant.Telemetry.ExportHeaders, "x-api-key=A,x-tenant=B");
            var first = Flush(host, system);
            AssertAllSignals(first, "x-api-key", "A");
            AssertAllSignals(first, "x-tenant", "B");
            Assert.All(first, request => Assert.Equal("application/x-protobuf", request.ContentType));
            Assert.Empty(deployment.Requests);

            await SetAsync(host, SettingConstant.Telemetry.ExportHeaders, "x-api-key=C");
            var second = Flush(host, system);
            AssertAllSignals(second, "x-api-key", "C");
            Assert.All(second, request => Assert.Empty(request.Header("x-tenant")));

            await SetAsync(host, SettingConstant.Telemetry.ExportHeaders, null);
            await SetAsync(host, SettingConstant.Telemetry.ExportEndpoint, null);
            var restored = Flush(host, deployment);
            AssertAllSignals(restored, "x-api-key", "deploy-a");
            AssertAllSignals(restored, "x-tenant", "deploy-b");
        }
        finally
        {
            await ClearAsync(host);
        }
    }

    /// <summary>只在系统设置里打开并填地址（部署未启用）：不重启，三路随后到达。</summary>
    [Fact]
    public async Task Enabling_in_system_settings_alone_starts_export_without_restart()
    {
        using var receiver = new OtlpReceiver();
        using var host = Host(new() { [TelemetryExportOptions.EndpointKey] = "" });
        await ClearAsync(host);
        try
        {
            var definitions = host.Services.GetRequiredService<ISettingDefinitionManager>();
            Assert.Equal("false", definitions.GetOrNull(SettingConstant.Telemetry.ExportEnabled)!.DefaultValue);
            Assert.Empty(Flush(host, receiver));

            await SetAsync(host, SettingConstant.Telemetry.ExportEndpoint, receiver.BaseAddress);
            // 只填地址不打开：开关仍取部署基线（未启用），不因有效配置里出现了地址而重算
            Assert.Empty(Flush(host, receiver));

            await SetAsync(host, SettingConstant.Telemetry.ExportEnabled, "true");
            AssertAllSignals(Flush(host, receiver));
        }
        finally
        {
            await ClearAsync(host);
        }
    }

    /// <summary>地址只收绝对 http/https 基地址，认证头只收逗号分隔的 name=value；错误不回显认证头。</summary>
    [Theory]
    [InlineData(SettingConstant.Telemetry.ExportEndpoint, "collector:4318", AppSettingErrorCodes.TelemetryEndpointInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportEndpoint, "ftp://collector", AppSettingErrorCodes.TelemetryEndpointInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportEndpoint, "http://collector:4318/?key=secret", AppSettingErrorCodes.TelemetryEndpointInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportHeaders, "x-api-key", AppSettingErrorCodes.TelemetryHeadersInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportHeaders, "=secret", AppSettingErrorCodes.TelemetryHeadersInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportHeaders, "bad name=secret", AppSettingErrorCodes.TelemetryHeadersInvalid)]
    [InlineData(SettingConstant.Telemetry.ExportHeaders, "x-a=1,,x-b=2", AppSettingErrorCodes.TelemetryHeadersInvalid)]
    public async Task Malformed_values_are_rejected_at_write(string name, string value, string code)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => SetAsync(factory, name, value));

        Assert.Equal(code, error.Code);
        if (name == SettingConstant.Telemetry.ExportHeaders)
            Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SettingConstant.Telemetry.ExportEndpoint, "https://otlp.example.com/tenant-a/otlp/")]
    [InlineData(SettingConstant.Telemetry.ExportHeaders, "x-api-key=a%3Db, Authorization=Bearer token")]
    public async Task Well_formed_values_are_accepted(string name, string value)
    {
        await SetAsync(factory, name, value);
        await SetAsync(factory, name, null);
    }

    // 部署值经 UseSetting 注入：导出开关的部署基线在注册阶段求值
    /// <summary>
    /// 与 SDK 默认客户端一致不跟随重定向：跟随会把 POST 改成 GET，落到登录页的 200 被当成导出成功，
    /// 数据实际没到 Collector。
    /// </summary>
    [Fact]
    public async Task Redirects_from_the_endpoint_are_not_followed()
    {
        using var receiver = new OtlpReceiver(redirectSignals: true);
        using var host = Host(new() { [TelemetryExportOptions.EndpointKey] = receiver.BaseAddress });
        await ClearAsync(host);

        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CompanyName.ProjectName.TelemetryExportTests")
            .LogWarning("Telemetry redirect probe");
        host.Services.GetRequiredService<LoggerProvider>().ForceFlush();

        Assert.Contains("/v1/logs", receiver.Requests.Select(request => request.Path));
        Assert.DoesNotContain("/login", receiver.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// 构建导出管道不得解析宿主生命周期：日志工厂构建期间解析会经日志依赖重入，得到第二个实例，
    /// 停机信号停掉的不是 <c>app.Run()</c> 等待的那个，进程收到 SIGTERM 后不退出。
    /// 用带默认日志与控制台生命周期的真实宿主核对：容器里的生命周期就是宿主通知启动的那个，发送门槛随之打开。
    /// </summary>
    [Fact]
    public async Task The_host_lifetime_is_a_single_instance_and_opens_the_export_gate()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            Configuration = new ConfigurationManager()
        });
        builder.AddMyProjectTelemetry();
        using var app = builder.Build();
        var gate = app.Services.GetRequiredService<TelemetryExportGate>();
        Assert.False(gate.IsOpen);

        await app.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
            Assert.True(gate.IsOpen);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private WebApplicationFactory<Program> Host(Dictionary<string, string?> deployment, StartupFlushProbe? probe = null)
        => factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in deployment)
                builder.UseSetting(key, value);
            if (probe is not null)
                builder.ConfigureTestServices(services => services.ConfigureDbContext<MyProjectDbContext>(
                    (serviceProvider, options) => options.AddInterceptors(probe.Bind(serviceProvider))));
        });

    private static async Task ClearAsync(WebApplicationFactory<Program> host)
    {
        await SetAsync(host, SettingConstant.Telemetry.ExportEnabled, null);
        await SetAsync(host, SettingConstant.Telemetry.ExportEndpoint, null);
        await SetAsync(host, SettingConstant.Telemetry.ExportHeaders, null);
    }

    // 宿主级设置经写入入口提交：提交后本进程立即应用到配置源
    private static async Task SetAsync(WebApplicationFactory<Program> host, string name, string? value)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await scope.ServiceProvider.GetRequiredService<ISettingManager>().SetAsync(name, value, SettingScopes.Host);
        await unitOfWork.CompleteAsync();
    }

    /// <summary>三路各产生一条数据并冲刷，返回本次冲刷期间接收端收到的请求。</summary>
    // 先冲刷一次收尾在途的导出：指标的周期导出可能在改设置之前就已按旧值发出、在计数之后才收到，
    // 不先收尾会被算进这一批。冲刷要等导出完成，接收端在应答之前已记下请求，所以收尾后再计数。
    private static List<OtlpRequest> Flush(WebApplicationFactory<Program> host, OtlpReceiver receiver)
    {
        FlushAll(host.Services);
        var before = receiver.Requests.Count;
        FlushAll(host.Services);
        return receiver.Requests.Skip(before).ToList();
    }

    private static void FlushAll(IServiceProvider services)
    {
        services.GetRequiredService<ILoggerFactory>().CreateLogger("CompanyName.ProjectName.TelemetryExportTests")
            .LogWarning("Telemetry export probe");
        using (Source.StartActivity("telemetry-export-probe")) { }
        Assert.True(services.GetRequiredService<LoggerProvider>().ForceFlush());
        Assert.True(services.GetRequiredService<TracerProvider>().ForceFlush());
        Assert.True(services.GetRequiredService<MeterProvider>().ForceFlush());
    }

    // 三路都到达，路径为 /v1/{signal}；给了头名时每个请求里该头恰好一份且等于期望值
    private static void AssertAllSignals(List<OtlpRequest> requests, string? header = null, string? value = null)
    {
        Assert.Equal(["/v1/logs", "/v1/metrics", "/v1/traces"], requests.Select(request => request.Path).Distinct().Order());
        if (header is not null)
            Assert.All(requests, request => Assert.Equal([value!], request.Header(header)));
    }

    /// <summary>首次读取设置表时冲刷三路：此刻数据库里的设置还没加载，宿主也还没启动完成。</summary>
    private sealed class StartupFlushProbe(OtlpReceiver receiver) : DbCommandInterceptor
    {
        private IServiceProvider? services;
        private int flushed;

        public bool Flushed => flushed == 1;
        public bool HostStartedAtFlush { get; private set; }
        public int ReceivedDuringStartup { get; private set; }

        public StartupFlushProbe Bind(IServiceProvider serviceProvider)
        {
            // 只用来取单例（日志、追踪、指标提供程序与宿主生命周期），哪个作用域都一样
            services = serviceProvider;
            return this;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            FlushOnFirstSettingsRead(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            FlushOnFirstSettingsRead(command);
            return ValueTask.FromResult(result);
        }

        private void FlushOnFirstSettingsRead(DbCommand command)
        {
            if (!command.CommandText.Contains("\"SettingRecord\"", StringComparison.Ordinal)
                || Interlocked.Exchange(ref flushed, 1) == 1)
                return;

            HostStartedAtFlush = services!.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested;
            FlushAll(services!);
            ReceivedDuringStartup = receiver.Requests.Count;
        }
    }
}
