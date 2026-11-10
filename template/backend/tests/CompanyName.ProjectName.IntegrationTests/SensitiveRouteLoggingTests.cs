using System.Diagnostics;
using System.Net;
using System.Text;
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Options;
using Leistd.ExceptionHandling;
using Leistd.Tracing.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>路径含机密的端点：原始值不进任何日志与追踪，完成日志与 <c>url.path</c> 记路由模板；未声明的端点照常记原始路径。</summary>
/// <remarks>
/// 每个用例用一个新的随机机密，在宿主收集到的全部日志（消息、状态、作用域、异常）与真实导出到本机接收端的 OTLP 正文里查找。
/// 导出正文不解码：protobuf 的字符串字段以 UTF-8 原样出现，属性按"键之后紧跟值"匹配；对照端点证明同样的查找能找到原始值。
/// </remarks>
public sealed class SensitiveRouteLoggingTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string CompletionCategory = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";
    private const string MarkedTemplate = "/" + SensitiveRouteProbeController.BasePath + "/marked/{provider}/{routeKey}";
    private const string ExceptionHandlerCategory = "Leistd.ExceptionHandling.AspNetCore.Handlers.BusinessExceptionHandler";

    [Fact]
    public async Task A_marked_route_keeps_the_secret_out_of_every_log_and_span()
    {
        using var receiver = new OtlpReceiver();
        using var host = Host(receiver);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var secret = NewSecret();

        using var response = await client.GetAsync($"{SensitiveRouteProbeController.BasePath}/marked/sendgrid/{secret}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = RequestRecords(host, response);
        var completion = Assert.Single(records, record => record.Category == CompletionCategory);
        Assert.Equal(MarkedTemplate, LogRecordFields.State(completion, "Path"));
        // 业务日志照常带关联标识与 TraceId，只是不再带 Hosting 的原始路径作用域
        var business = Assert.Single(records, record => record.Message.StartsWith(SensitiveRouteProbeController.HandledMessage, StringComparison.Ordinal));
        Assert.NotNull(LogRecordFields.Scope(business, "TraceId"));
        Assert.Null(LogRecordFields.Scope(business, "RequestPath"));
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), record => Mentions(record, secret));

        var traces = await ExportedAsync(host, receiver, "/v1/traces", body => HasAttribute(body, "url.path", MarkedTemplate));
        var logs = await ExportedAsync(host, receiver, "/v1/logs", body => HasAttribute(body, "Path", MarkedTemplate));
        Assert.All(traces.Concat(logs), body => Assert.False(Contains(body, secret), "The route secret reached an OTLP export"));
        Assert.All(logs, body => Assert.False(Contains(body, "RequestPath"), "The hosting request scope reached an OTLP export"));
    }

    /// <summary>
    /// 失败的请求：业务错误（4xx）与未处理异常（5xx）经全局异常处理写出，任何日志与追踪都不出现机密原值，
    /// 完成日志与 <c>url.path</c> 记模板，异常处理器的日志记路由模板。
    /// </summary>
    [Theory]
    [InlineData("marked-business", HttpStatusCode.BadRequest)]
    [InlineData("marked-fail", HttpStatusCode.InternalServerError)]
    public async Task A_failed_request_on_a_marked_route_keeps_the_secret_out_of_every_log_and_span(string action, HttpStatusCode status)
    {
        using var receiver = new OtlpReceiver();
        using var host = Host(receiver);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var secret = NewSecret();
        var template = $"/{SensitiveRouteProbeController.BasePath}/{action}/{{routeKey}}";

        using var response = await client.GetAsync($"{SensitiveRouteProbeController.BasePath}/{action}/{secret}");

        Assert.Equal(status, response.StatusCode);
        var records = RequestRecords(host, response);
        var completion = Assert.Single(records, record => record.Category == CompletionCategory);
        Assert.Equal(template, LogRecordFields.State(completion, "Path"));
        var handled = Assert.Single(records, record => record.Category == ExceptionHandlerCategory);
        Assert.Equal(template.TrimStart('/'), LogRecordFields.State(handled, "Route"));
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), record => Mentions(record, secret));

        var traces = await ExportedAsync(host, receiver, "/v1/traces", body => HasAttribute(body, "url.path", template));
        var logs = await ExportedAsync(host, receiver, "/v1/logs", body => HasAttribute(body, "Path", template));
        Assert.All(traces.Concat(logs), body => Assert.False(Contains(body, secret), "The route secret reached an OTLP export"));
    }

    /// <summary>对照：未声明的同形端点照常记原始路径，上面"找不到"不是因为查找方式看不见路径。</summary>
    [Fact]
    public async Task An_unmarked_route_records_the_raw_path()
    {
        using var receiver = new OtlpReceiver();
        using var host = Host(receiver);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var secret = NewSecret();
        var rawPath = $"/{SensitiveRouteProbeController.BasePath}/control/sendgrid/{secret}";

        using var response = await client.GetAsync(rawPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completion = Assert.Single(RequestRecords(host, response), record => record.Category == CompletionCategory);
        Assert.Equal(rawPath, LogRecordFields.State(completion, "Path"));
        Assert.True(Mentions(completion, secret));
        await ExportedAsync(host, receiver, "/v1/traces", body => HasAttribute(body, "url.path", rawPath));
        await ExportedAsync(host, receiver, "/v1/logs", body => HasAttribute(body, "Path", rawPath));
    }

    private WebApplicationFactory<Program> Host(OtlpReceiver receiver) => factory.WithWebHostBuilder(builder => builder
        .UseSetting(TelemetryExportOptions.EndpointKey, receiver.BaseAddress)
        .ConfigureServices(services => services.AddControllers()
            .AddApplicationPart(typeof(SensitiveRouteProbeController).Assembly)));

    private static string NewSecret() => $"rk{Guid.NewGuid():N}";

    private static FakeLogRecord[] RequestRecords(WebApplicationFactory<Program> host, HttpResponseMessage response)
    {
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdConstants.DefaultHeaderName));
        return host.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => LogRecordFields.Scope(record, CorrelationIdConstants.LogKey) == correlationId)
            .ToArray();
    }

    // 消息、结构化状态、每个作用域（键值与文本形态）与异常文本
    private static bool Mentions(FakeLogRecord record, string value)
    {
        bool Has(string? text) => text?.Contains(value, StringComparison.Ordinal) == true;

        return Has(record.Message) || Has(record.Exception?.ToString())
            || record.StructuredState?.Any(pair => Has(pair.Key) || Has(pair.Value)) == true
            || record.Scopes.Any(scope => Has(scope?.ToString())
                || scope is IEnumerable<KeyValuePair<string, object?>> pairs && pairs.Any(pair => Has(pair.Key) || Has(pair.Value?.ToString())));
    }

    /// <summary>
    /// 反复冲刷，直到该信号收到满足条件的正文，返回此时该信号的全部正文。
    /// 服务端 span 在响应发出后才结束，冲刷一次可能早于它。
    /// </summary>
    private static async Task<byte[][]> ExportedAsync(
        WebApplicationFactory<Program> host, OtlpReceiver receiver, string signal, Func<byte[], bool> expected)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush());
            Assert.True(host.Services.GetRequiredService<LoggerProvider>().ForceFlush());
            var bodies = receiver.Requests.Where(request => request.Path == signal).Select(request => request.Body).ToArray();
            if (bodies.Any(expected))
                return bodies;
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), $"No {signal} export carried the expected attribute");
            await Task.Delay(100);
        }
    }

    private static bool Contains(byte[] body, string text) => body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;

    // KeyValue{key, value: AnyValue{string_value}}：值的字节紧跟在键之后，中间只隔字段标签与长度（不超过 12 字节）
    private static bool HasAttribute(byte[] body, string key, string value)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        for (var start = 0; start < body.Length;)
        {
            var found = body.AsSpan(start).IndexOf(keyBytes);
            if (found < 0)
                return false;
            var afterKey = start + found + keyBytes.Length;
            var window = body.AsSpan(afterKey, Math.Min(12 + valueBytes.Length, body.Length - afterKey));
            var at = window.IndexOf(valueBytes);
            if (at is >= 0 and <= 12)
                return true;
            start = afterKey;
        }

        return false;
    }
}

/// <summary>路径含机密的探针端点：声明的三个（成功、业务错误、未处理异常）与一个同形的未声明对照。</summary>
[ApiController]
[Route(BasePath)]
public sealed class SensitiveRouteProbeController(ILogger<SensitiveRouteProbeController> logger) : ControllerBase
{
    public const string BasePath = "api/__tests/sensitive-route";
    public const string HandledMessage = "Sensitive route probe handled";

    [SensitiveRoute]
    [HttpGet("marked/{provider}/{routeKey}")]
    public IActionResult Marked(string provider, string routeKey) => Handle(provider, routeKey);

    [SensitiveRoute]
    [HttpGet("marked-business/{routeKey}")]
    public IActionResult MarkedBusiness(string routeKey)
        => throw new BusinessException("SensitiveRouteProbe:Rejected", $"Probe rejected ({routeKey.Length} chars).");

    [SensitiveRoute]
    [HttpGet("marked-fail/{routeKey}")]
    public IActionResult MarkedFail(string routeKey)
        => throw new InvalidOperationException($"Sensitive route probe failure ({routeKey.Length} chars).");

    [HttpGet("control/{provider}/{routeKey}")]
    public IActionResult Control(string provider, string routeKey) => Handle(provider, routeKey);

    // 业务日志只记服务商与机密长度：机密能否经作用域等框架字段混进来，是用例要核对的
    private OkResult Handle(string provider, string routeKey)
    {
        logger.LogInformation(HandledMessage + " for {Provider} ({Length} chars)", provider, routeKey.Length);
        return Ok();
    }
}
