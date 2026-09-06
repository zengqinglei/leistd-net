using System.Net;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Handlers;
using Microsoft.Extensions.Logging;
using Xunit;
using Leistd.TestBase.Doubles;
using Leistd.ServiceClient.Tests.TestDoubles;

namespace Leistd.ServiceClient.Tests.Core;

public class LoggingHandlerTests
{
    private static (HttpMessageInvoker Invoker, ListLogger Logger) Create(
        HttpMessageHandler inner, bool logPayloads = false, int maxPayloadLength = 4096)
    {
        var logger = new ListLogger();
        var handler = new ServiceClientLoggingHandler(logger, "DemoService", logPayloads, maxPayloadLength)
        {
            InnerHandler = inner,
        };
        return (new HttpMessageInvoker(handler), logger);
    }

    [Fact]
    public async Task 成功调用_输出Information摘要()
    {
        var (invoker, logger) = Create(new CapturingHttpMessageHandler());

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("DemoService", entry.Message);
        Assert.Contains("200", entry.Message);
    }

    [Fact]
    public async Task 非2xx响应_输出Warning摘要()
    {
        var (invoker, logger) = Create(new CapturingHttpMessageHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("500"));
    }

    [Fact]
    public async Task 传输层异常_包装为ServiceClientException并输出Error()
    {
        var (invoker, logger) = Create(new ThrowingHttpMessageHandler(_ => new HttpRequestException("connection refused")));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

        Assert.Contains("connection refused", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task 调用方主动取消_不包装原样上抛()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (invoker, _) = Create(new ThrowingHttpMessageHandler(ct => new TaskCanceledException("canceled", null, ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), cts.Token));
    }

    [Fact]
    public async Task 开启载荷日志_记录请求响应体并截断_敏感头脱敏()
    {
        var (invoker, logger) = Create(
            new CapturingHttpMessageHandler
            {
                Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new string('r', 100)),
                },
            },
            logPayloads: true,
            maxPayloadLength: 10);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://demo/api")
        {
            Content = new StringContent("{\"secret\":1}"),
        };
        request.Headers.Add("Authorization", "Bearer top-secret");
        await invoker.SendAsync(request, CancellationToken.None);

        var debugEntries = logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
        Assert.Equal(2, debugEntries.Count);
        Assert.DoesNotContain("top-secret", debugEntries[0].Message);
        Assert.Contains("Authorization: ***", debugEntries[0].Message);
        Assert.Contains("{\"secret\":", debugEntries[0].Message); // 请求体截断为 10 字符
        Assert.DoesNotContain("{\"secret\":1}", debugEntries[0].Message);
        Assert.Contains(new string('r', 10), debugEntries[1].Message); // 响应体截断
        Assert.DoesNotContain(new string('r', 11), debugEntries[1].Message);
    }
}
