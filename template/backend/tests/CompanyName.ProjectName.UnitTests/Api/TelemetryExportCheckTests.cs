using System.Net;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Hosting;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>系统设置的 OTLP 测试：用这一次读到的存储值（缺项取部署配置）发空的追踪导出请求，按状态码归类。</summary>
public sealed class TelemetryExportCheckTests
{
    private readonly RecordingHandler _handler = new();

    [Theory]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.Accepted, null)]
    [InlineData(HttpStatusCode.Unauthorized, SettingCheckFailureCodes.AuthRejected)]
    [InlineData(HttpStatusCode.Forbidden, SettingCheckFailureCodes.AuthRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, SettingCheckFailureCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, SettingCheckFailureCodes.UnexpectedStatus)]
    public async Task The_response_status_decides_the_outcome(HttpStatusCode status, string? failureCode)
    {
        _handler.Status = status;

        var outcome = await Check().ExecuteAsync(
            Context(new() { [SettingConstant.Telemetry.ExportEndpoint] = "http://collector:4318" }), CancellationToken.None);

        Assert.Equal(failureCode, outcome.FailureCode);
        Assert.Equal(failureCode is null, outcome.Passed);
    }

    // 与导出时同一写法：信号路径接在地址后面，认证头按 key=value 解析、值按 URL 编码解码
    [Fact]
    public async Task An_empty_trace_export_goes_to_the_endpoint_with_the_stored_headers()
    {
        await Check().ExecuteAsync(Context(new()
        {
            [SettingConstant.Telemetry.ExportEndpoint] = "https://otlp.example.com/otlp/",
            [SettingConstant.Telemetry.ExportHeaders] = "api-key=s%3Dcret,x-team=ops",
        }), CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://otlp.example.com/otlp/v1/traces", request.Uri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(["s=cret"], request.Headers["api-key"]);
        Assert.Equal(["ops"], request.Headers["x-team"]);
        Assert.Equal(0, request.ContentLength);
    }

    // 没有存值的项取部署配置；存着的值与部署值逐项组合，不取进程里叠加了覆盖的配置
    [Fact]
    public async Task Values_not_stored_fall_back_to_the_deployment_configuration()
    {
        await Check(new() { [SettingConstant.Telemetry.ExportHeaders] = "api-key=deployed" })
            .ExecuteAsync(Context(new() { [SettingConstant.Telemetry.ExportEndpoint] = "http://stored:4318" }), CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("http://stored:4318/v1/traces", request.Uri);
        Assert.Equal(["deployed"], request.Headers["api-key"]);
    }

    [Fact]
    public async Task Without_an_endpoint_nothing_is_sent()
    {
        var outcome = await Check().ExecuteAsync(Context([]), CancellationToken.None);

        Assert.Equal(SettingCheckFailureCodes.NotConfigured, outcome.FailureCode);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task A_connection_failure_is_unreachable()
    {
        _handler.Failure = new HttpRequestException("connection refused");

        var outcome = await Check().ExecuteAsync(
            Context(new() { [SettingConstant.Telemetry.ExportEndpoint] = "http://collector:4318" }), CancellationToken.None);

        Assert.Equal(SettingCheckFailureCodes.Unreachable, outcome.FailureCode);
    }

    // 存着的认证头解不开时不拿部署配置里的值去测：读取抛出，由设置组件记为 secret-unavailable
    [Fact]
    public async Task A_stored_header_that_cannot_be_decrypted_is_not_replaced_by_the_deployed_one()
    {
        var context = Context(
            new() { [SettingConstant.Telemetry.ExportEndpoint] = "http://collector:4318" },
            undecryptable: SettingConstant.Telemetry.ExportHeaders);

        await Assert.ThrowsAsync<SettingDecryptionException>(
            () => Check(new() { [SettingConstant.Telemetry.ExportHeaders] = "api-key=old" }).ExecuteAsync(context, CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    private TelemetryExportCheck Check(Dictionary<string, string?>? deployed = null)
        => new(new Baseline(deployed ?? []), new Clients(_handler));

    // 本层读取：存储值优先；解不开的项抛出，与设置组件的读取一致
    private static SettingCheckContext Context(Dictionary<string, string?> stored, string? undecryptable = null)
        => new(SettingConstant.Telemetry.ExportEndpoint, SettingScopes.Host, (name, _) => name == undecryptable
            ? Task.FromException<string?>(new SettingDecryptionException(name, [name], keyId: null, new InvalidOperationException()))
            : Task.FromResult(stored.GetValueOrDefault(name)));

    private sealed class Baseline(Dictionary<string, string?> values) : IHostSettingBaseline
    {
        public string? GetDeploymentValue(string settingName) => values.GetValueOrDefault(settingName);
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record RecordedRequest(string Uri, HttpMethod Method, IReadOnlyDictionary<string, string[]> Headers, long? ContentLength);

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
                body?.LongLength));
            if (Failure is not null)
            {
                throw Failure;
            }

            return new HttpResponseMessage(Status);
        }
    }
}
