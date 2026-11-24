using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Errors;
using CompanyName.ProjectName.Application.Settings.Options;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>运维分组的导出测试：用当前生效的地址与认证头发空的追踪导出，按结果给出原因码。</summary>
public sealed class TelemetryExportProbeTests
{
    private const string Endpoint = "http://collector:4318";

    private readonly RecordingHandler _handler = new();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task A_2xx_answer_passes(HttpStatusCode status)
    {
        _handler.Status = status;

        await Probe(new() { Endpoint = Endpoint }).TestAsync();

        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AppSettingErrorCodes.TelemetryExportAuthRejected)]
    [InlineData(HttpStatusCode.Forbidden, AppSettingErrorCodes.TelemetryExportAuthRejected)]
    [InlineData(HttpStatusCode.Found, AppSettingErrorCodes.TelemetryExportUnexpectedStatus)]
    [InlineData(HttpStatusCode.NotFound, AppSettingErrorCodes.TelemetryExportUnexpectedStatus)]
    [InlineData(HttpStatusCode.TooManyRequests, AppSettingErrorCodes.TelemetryExportUnexpectedStatus)]
    [InlineData(HttpStatusCode.InternalServerError, AppSettingErrorCodes.TelemetryExportUnexpectedStatus)]
    public async Task Other_answers_report_the_reason_and_the_status(HttpStatusCode status, string code)
    {
        _handler.Status = status;

        var error = await Assert.ThrowsAsync<BusinessException>(() => Probe(new() { Endpoint = Endpoint }).TestAsync());

        Assert.Equal(code, error.Code);
        Assert.Equal((int)status, error.LocalizationData["Status"]);
    }

    // 与导出时同一写法：信号路径接在地址后面，认证头按 key=value 解析、值按 URL 编码解码
    [Fact]
    public async Task An_empty_trace_export_goes_to_the_effective_endpoint_with_its_headers()
    {
        await Probe(new()
        {
            Endpoint = " https://otlp.example.com/otlp/ ",
            Headers = "api-key=s%3Dcret,x-team=ops"
        }).TestAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://otlp.example.com/otlp/v1/traces", request.Uri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(["s=cret"], request.Headers["api-key"]);
        Assert.Equal(["ops"], request.Headers["x-team"]);
        Assert.Equal("application/x-protobuf", request.ContentType);
        Assert.Equal(0, request.ContentLength);
    }

    // 校验允许名称大小写不同的同名头；导出时同名只发最后一个值，测试必须发出同一份，否则旧值会让测试报认证被拒
    [Fact]
    public async Task Headers_with_the_same_name_in_different_case_send_only_the_last_value()
    {
        await Probe(new() { Endpoint = Endpoint, Headers = "x-api-key=old,X-Api-Key=current" }).TestAsync();

        Assert.Equal(["current"], Assert.Single(_handler.Requests).Headers["x-api-key"]);
    }

    // 开关关着也测：开启之前先验证地址与认证头
    [Fact]
    public async Task The_export_switch_being_off_does_not_stop_the_test()
    {
        await Probe(new() { Enabled = false, Endpoint = Endpoint }).TestAsync();

        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_an_endpoint_nothing_is_sent(string? endpoint)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Probe(new() { Enabled = true, Endpoint = endpoint }).TestAsync());

        Assert.Equal(AppSettingErrorCodes.TelemetryExportNotConfigured, error.Code);
        Assert.Empty(_handler.Requests);
    }

    // 部署关闭了 SDK：导出选项没有注册，即使取到了地址也不测，答复本部署不可用而不是"未配置"
    [Fact]
    public async Task A_disabled_sdk_is_reported_as_unavailable()
    {
        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Probe(new() { Endpoint = Endpoint }, exportConfigurable: false).TestAsync());

        Assert.Equal(AppSettingErrorCodes.TelemetryExportUnavailable, error.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task A_connection_failure_is_unreachable()
    {
        _handler.Failure = new HttpRequestException("connection refused");

        var error = await Assert.ThrowsAsync<BusinessException>(() => Probe(new() { Endpoint = Endpoint }).TestAsync());

        Assert.Equal(AppSettingErrorCodes.TelemetryExportUnreachable, error.Code);
    }

    // 客户端超时表现为未经调用方取消的 TaskCanceledException
    [Fact]
    public async Task A_timeout_is_unreachable()
    {
        _handler.Failure = new TaskCanceledException("timeout", new TimeoutException());

        var error = await Assert.ThrowsAsync<BusinessException>(() => Probe(new() { Endpoint = Endpoint }).TestAsync());

        Assert.Equal(AppSettingErrorCodes.TelemetryExportUnreachable, error.Code);
    }

    // 调用方放弃请求不是对方不可达，照常取消
    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _handler.Failure = new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Probe(new() { Endpoint = Endpoint }).TestAsync(cancelled.Token));
    }

    // 部署配置里写坏的地址：不发送，按连不上报
    [Theory]
    [InlineData("collector:4318")]
    [InlineData("ftp://collector")]
    public async Task A_malformed_deployment_endpoint_is_unreachable(string endpoint)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => Probe(new() { Endpoint = endpoint }).TestAsync());

        Assert.Equal(AppSettingErrorCodes.TelemetryExportUnreachable, error.Code);
        Assert.Empty(_handler.Requests);
    }

    private TelemetryExportProbe Probe(TelemetryExportOptions current, bool exportConfigurable = true)
        => new(
            new FixedMonitor(current),
            Options.Create(new TelemetrySettingsOptions { ExportConfigurable = exportConfigurable }),
            new Clients(_handler));

    private sealed class FixedMonitor(TelemetryExportOptions value) : IOptionsMonitor<TelemetryExportOptions>
    {
        public TelemetryExportOptions CurrentValue => value;

        public TelemetryExportOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TelemetryExportOptions, string?> listener) => null;
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(TelemetryExportProbe.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed record RecordedRequest(
        string Uri, HttpMethod Method, IReadOnlyDictionary<string, string[]> Headers, string? ContentType, long? ContentLength);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public Exception? Failure { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Method,
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType,
                body?.LongLength));
            if (Failure is not null)
            {
                throw Failure;
            }

            return new HttpResponseMessage(Status);
        }
    }
}
