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
    public async Task Successful_call_logs_an_information_summary()
    {
        var (invoker, logger) = Create(new CapturingHttpMessageHandler());

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("DemoService", entry.Message);
        Assert.Contains("200", entry.Message);
    }

    [Fact]
    public async Task Non_success_response_logs_a_warning_summary()
    {
        var (invoker, logger) = Create(new CapturingHttpMessageHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("500"));
    }

    [Fact]
    public async Task Transport_failure_is_wrapped_and_logged_as_error()
    {
        var (invoker, logger) = Create(new ThrowingHttpMessageHandler(_ =>
            new HttpRequestException(HttpRequestError.ConnectionError, "connection refused")));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

        Assert.Contains("connection refused", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(ServiceClientFailureKind.Unavailable, exception.FailureKind);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Unexpected_handler_failure_is_not_reported_as_upstream_unavailability()
    {
        var (invoker, _) = Create(new ThrowingHttpMessageHandler(_ => new InvalidOperationException("handler defect")));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

        Assert.Equal(ServiceClientFailureKind.Unknown, exception.FailureKind);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task Non_connection_http_failure_is_not_reported_as_temporary_unavailability(HttpRequestError error)
    {
        var (invoker, _) = Create(new ThrowingHttpMessageHandler(_ =>
            new HttpRequestException(error, "request failed")));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

        Assert.Equal(ServiceClientFailureKind.Unknown, exception.FailureKind);
    }

    [Theory]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ResponseEnded)]
    public async Task Malformed_or_incomplete_upstream_response_is_classified_as_invalid_response(HttpRequestError error)
    {
        foreach (var transportException in new Exception[]
                 {
                     new HttpRequestException(error, "upstream response failed"),
                     new HttpIOException(error, "upstream response failed")
                 })
        {
            var (invoker, _) = Create(new ThrowingHttpMessageHandler(_ => transportException));

            var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
                invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

            Assert.Equal(ServiceClientFailureKind.InvalidResponse, exception.FailureKind);
            Assert.Same(transportException, exception.InnerException);
        }
    }

    [Theory]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ResponseEnded)]
    public async Task Debug_response_body_failure_is_classified_logged_and_disposes_the_response(HttpRequestError error)
    {
        var content = new ThrowingHttpContent(new HttpIOException(error, "response body ended"));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var (invoker, logger) = Create(new CapturingHttpMessageHandler
        {
            Responder = _ => response,
        }, logPayloads: true);

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None));

        Assert.Equal(ServiceClientFailureKind.InvalidResponse, exception.FailureKind);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.True(content.IsDisposed);
        Assert.Single(logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("response body read failed"));
    }

    [Fact]
    public async Task Caller_cancellation_during_debug_response_body_read_is_not_logged_as_a_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new ThrowingHttpContent(new OperationCanceledException(cancellation.Token));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var (invoker, logger) = Create(new CapturingHttpMessageHandler
        {
            Responder = _ =>
            {
                cancellation.Cancel();
                return response;
            },
        }, logPayloads: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), cancellation.Token));

        Assert.True(content.IsDisposed);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_unwrapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (invoker, _) = Create(new ThrowingHttpMessageHandler(ct => new TaskCanceledException("canceled", null, ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), cts.Token));
    }

    [Fact]
    public async Task Payload_logging_truncates_bodies_and_redacts_sensitive_headers()
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
