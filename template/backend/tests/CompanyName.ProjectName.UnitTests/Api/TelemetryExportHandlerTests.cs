using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
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

    private static (HttpMessageInvoker Invoker, RecordingHandler Inner, MutableMonitor Options, TelemetryExportGate Gate) Create(
        ExportValue value, bool started = true)
    {
        var options = new MutableMonitor(value);
        var gate = new TelemetryExportGate();
        if (started)
            gate.Open();
        var inner = new RecordingHandler();
        var handler = new TelemetryExportHandler(options, gate) { InnerHandler = inner };
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

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase)));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
