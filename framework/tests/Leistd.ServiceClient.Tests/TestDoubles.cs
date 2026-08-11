using System.Net;
using Microsoft.Extensions.Logging;

namespace Leistd.ServiceClient.Tests;

/// <summary>
/// 捕获出站请求并按委托应答的终端处理器（替代真实网络）。
/// </summary>
internal sealed class CaptureHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> RequestBodies { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = Responder(request);
        response.RequestMessage = request;
        return response;
    }
}

/// <summary>
/// 抛出异常的终端处理器。
/// </summary>
internal sealed class ThrowingHandler(Func<CancellationToken, System.Exception> exceptionFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw exceptionFactory(cancellationToken);
}

/// <summary>
/// 收集日志条目的 Logger。
/// </summary>
internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message, System.Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, System.Exception? exception,
        Func<TState, System.Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
