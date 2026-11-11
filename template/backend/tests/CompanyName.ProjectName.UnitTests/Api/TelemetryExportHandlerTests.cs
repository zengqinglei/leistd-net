using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>OTLP 发送层：启动前与未启用时不发网络，启用时按当前值改写地址并独占写入认证头。</summary>
/// <remarks>每条用例同步与异步两条发送路径各跑一次：SDK 在 .NET 上走同步发送。</remarks>
public sealed class TelemetryExportHandlerTests
{
    private const string SdkRequest = "http://localhost:4318/v1/";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Requests_before_the_host_has_started_are_dropped(bool sync)
    {
        var (invoker, inner, _, gate) = Create(new() { Enabled = true, Endpoint = "http://collector:4318" }, started: false);

        using var dropped = await SendAsync(invoker, "traces", sync);
        gate.Open();
        using var sent = await SendAsync(invoker, "traces", sync);

        Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
        Assert.Empty(await dropped.Content.ReadAsByteArrayAsync());
        Assert.Single(inner.Requests);
    }

    [Theory]
    [InlineData(false, "http://collector:4318", true)]
    [InlineData(false, "http://collector:4318", false)]
    [InlineData(true, null, true)]
    [InlineData(true, "", false)]
    [InlineData(true, "  ", true)]
    public async Task Disabled_or_blank_endpoint_sends_nothing(bool enabled, string? endpoint, bool sync)
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = enabled, Endpoint = endpoint });

        using var response = await SendAsync(invoker, "logs", sync);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(inner.Requests);
    }

    [Theory]
    [InlineData("http://collector:4318", "logs", "http://collector:4318/v1/logs", true)]
    [InlineData("http://collector:4318/", "metrics", "http://collector:4318/v1/metrics", false)]
    [InlineData("https://otlp.example.com/tenant-a/otlp/", "traces", "https://otlp.example.com/tenant-a/otlp/v1/traces", true)]
    [InlineData(" https://otlp.example.com/otlp ", "logs", "https://otlp.example.com/otlp/v1/logs", false)]
    public async Task Enabled_export_goes_to_the_signal_path_under_the_current_endpoint(
        string endpoint, string signal, string expected, bool sync)
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = true, Endpoint = endpoint });

        using var _ = await SendAsync(invoker, signal, sync);

        Assert.Equal(expected, Assert.Single(inner.Requests).Uri);
    }

    /// <summary>认证头按当时的值完整替换：换值、去掉一个、清空，每个头都只有当前的一份。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Headers_follow_the_current_value_with_one_copy_each(bool sync)
    {
        var (invoker, inner, options, _) = Create(new()
        {
            Enabled = true,
            Endpoint = "http://collector:4318",
            Headers = "x-api-key=A, x-tenant=one%20two"
        });

        using (await SendAsync(invoker, "traces", sync)) { }
        options.CurrentValue = options.CurrentValue with { Headers = "x-api-key=B,x-tenant=three" };
        using (await SendAsync(invoker, "metrics", sync)) { }
        options.CurrentValue = options.CurrentValue with { Headers = "x-api-key=C" };
        using (await SendAsync(invoker, "logs", sync)) { }
        options.CurrentValue = options.CurrentValue with { Headers = null };
        using (await SendAsync(invoker, "logs", sync)) { }

        Assert.Equal(4, inner.Requests.Count);
        Assert.Equal(["A"], inner.Requests[0].Header("x-api-key"));
        Assert.Equal(["one two"], inner.Requests[0].Header("x-tenant"));
        Assert.Equal(["B"], inner.Requests[1].Header("x-api-key"));
        Assert.Equal(["three"], inner.Requests[1].Header("x-tenant"));
        Assert.Equal(["C"], inner.Requests[2].Header("x-api-key"));
        Assert.Empty(inner.Requests[2].Header("x-tenant"));
        Assert.Empty(inner.Requests[3].Header("x-api-key"));
        // SDK 自己的协议头原样保留
        Assert.All(inner.Requests, request => Assert.Equal(["sdk"], request.Header("User-Agent")));
    }

    /// <summary>请求里已有同名头时由当前值替换，不叠加。</summary>
    [Fact]
    public void An_existing_header_with_the_same_name_is_replaced()
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = true, Endpoint = "http://collector:4318", Headers = "x-api-key=current" });
        using var request = Request("traces");
        request.Headers.TryAddWithoutValidation("x-api-key", "stale");

        using var _ = invoker.Send(request, CancellationToken.None);

        Assert.Equal(["current"], Assert.Single(inner.Requests).Header("x-api-key"));
    }

    // 收集器拒绝（如认证头错误）时 SDK 只经 EventSource 报错：发送层要在进程日志里留一条，持续失败不刷屏，恢复时说一声
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_rejected_export_logs_one_warning_until_it_recovers(bool sync)
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = true, Endpoint = "http://collector:4318", Headers = "Authorization=Bearer secret-token" });
        inner.Status = HttpStatusCode.Unauthorized;

        (await SendAsync(invoker, "logs", sync)).Dispose();
        (await SendAsync(invoker, "logs", sync)).Dispose();
        inner.Status = HttpStatusCode.OK;
        (await SendAsync(invoker, "logs", sync)).Dispose();
        inner.Status = HttpStatusCode.Unauthorized;
        (await SendAsync(invoker, "logs", sync)).Dispose();

        Assert.Equal(
            [
                (LogLevel.Warning, "OTLP export of logs was rejected with HTTP 401; check the endpoint and headers. Repeated failures are not logged until it recovers."),
                (LogLevel.Information, "OTLP export of logs recovered."),
                (LogLevel.Warning, "OTLP export of logs was rejected with HTTP 401; check the endpoint and headers. Repeated failures are not logged until it recovers."),
            ],
            Logs.Entries.Select(entry => (entry.Level, entry.Message)));
        Assert.DoesNotContain(Logs.Entries, entry => entry.Message.Contains("secret-token", StringComparison.Ordinal));
    }

    // 各路信号分开记：日志被拒不能盖住追踪的状态
    [Fact]
    public async Task Failures_are_tracked_per_signal()
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = true, Endpoint = "http://collector:4318" });
        inner.Status = HttpStatusCode.Forbidden;

        (await SendAsync(invoker, "logs", sync: true)).Dispose();
        (await SendAsync(invoker, "traces", sync: true)).Dispose();

        Assert.Equal(2, Logs.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    // 连不上（地址错、收集器没起）同样要留痕；异常照原样交回 SDK，由它按自己的规则处理
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unreachable_endpoint_logs_a_warning_and_rethrows(bool sync)
    {
        var (invoker, inner, _, _) = Create(new() { Enabled = true, Endpoint = "http://collector:4318" });
        inner.Failure = new HttpRequestException("Connection refused");

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(invoker, "metrics", sync));

        var entry = Assert.Single(Logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("OTLP export of metrics could not be sent. Repeated failures are not logged until it recovers.", entry.Message);
        Assert.IsType<HttpRequestException>(entry.Exception);
    }

    private RecordingLoggerProvider Logs { get; } = new();

    private (HttpMessageInvoker Invoker, RecordingHandler Inner, MutableMonitor Options, TelemetryExportGate Gate) Create(
        ExportValue value, bool started = true)
    {
        var options = new MutableMonitor(value);
        var gate = new TelemetryExportGate();
        if (started)
            gate.Open();
        var inner = new RecordingHandler();
        var services = new ServiceCollection().AddLogging(logging => logging.AddProvider(Logs)).BuildServiceProvider();
        var handler = new TelemetryExportHandler(options, gate, new TelemetryExportFailureReporter(services)) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner, options, gate);
    }

    private static HttpRequestMessage Request(string signal)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SdkRequest + signal) { Content = new ByteArrayContent([1, 2, 3]) };
        request.Headers.TryAddWithoutValidation("User-Agent", "sdk");
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker, string signal, bool sync)
    {
        var request = Request(signal);
        return sync ? invoker.Send(request, CancellationToken.None) : await invoker.SendAsync(request, CancellationToken.None);
    }

    private sealed record ExportValue
    {
        public bool Enabled { get; init; }
        public string? Endpoint { get; init; }
        public string? Headers { get; init; }
    }

    private sealed class MutableMonitor(ExportValue value) : IOptionsMonitor<TelemetryExportOptions>
    {
        public ExportValue CurrentValue { get; set; } = value;

        TelemetryExportOptions IOptionsMonitor<TelemetryExportOptions>.CurrentValue
            => new() { Enabled = CurrentValue.Enabled, Endpoint = CurrentValue.Endpoint, Headers = CurrentValue.Headers };

        public TelemetryExportOptions Get(string? name) => ((IOptionsMonitor<TelemetryExportOptions>)this).CurrentValue;

        public IDisposable? OnChange(Action<TelemetryExportOptions, string?> listener) => null;
    }

    private sealed record RecordedRequest(string Uri, IReadOnlyDictionary<string, string[]> Headers)
    {
        public string[] Header(string name) => Headers.TryGetValue(name, out var values) ? values : [];
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;

        public Exception? Failure { get; set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase)));
            if (Failure is not null)
                throw Failure;
            return new HttpResponseMessage(Status);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
                Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public void Dispose()
        {
        }
    }
}
