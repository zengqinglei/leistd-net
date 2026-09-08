using System.Diagnostics;
using Leistd.ServiceClient.Exceptions;
using Microsoft.Extensions.Logging;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 记录服务调用摘要并规范化传输异常。
/// </summary>
/// <remarks>载荷日志仅在启用时输出，并对敏感头脱敏、对正文截断。</remarks>
public sealed class ServiceClientLoggingHandler(
    ILogger logger,
    string serviceName,
    bool logPayloads,
    int maxPayloadLength) : DelegatingHandler
{
    // 载荷日志中值被脱敏的请求/响应头（不区分大小写）。
    private static readonly HashSet<string> RedactedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        Constants.ServiceClientHeaders.UserId,
        Constants.ServiceClientHeaders.Username,
    };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        if (logPayloads && logger.IsEnabled(LogLevel.Debug))
        {
            var requestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug(
                "{Service} {Method} {Uri} request headers {Headers} request body {Body}",
                serviceName, request.Method, request.RequestUri,
                FormatHeaders(request.Headers), Truncate(requestBody));
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            var failedElapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogError(
                ex,
                "{Service} {Method} {Uri} invocation failed after {ElapsedMs}ms",
                serviceName, request.Method, request.RequestUri, (long)failedElapsed.TotalMilliseconds);

            // 主动取消和已规范化异常必须保留原始语义。
            if (ex is ServiceClientException ||
                (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                throw;
            }

            throw new ServiceClientException(
                $"{request.Method} {request.RequestUri} invocation failed: {ex.Message}", ex);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        logger.Log(
            response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning,
            "{Service} {Method} {Uri} responded {StatusCode} in {ElapsedMs}ms",
            serviceName, request.Method, request.RequestUri,
            (int)response.StatusCode, (long)elapsed.TotalMilliseconds);

        if (logPayloads && logger.IsEnabled(LogLevel.Debug) && response.Content is not null)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug(
                "{Service} {Method} {Uri} response body {Body}",
                serviceName, request.Method, request.RequestUri, Truncate(responseBody));
        }

        return response;
    }

    private string? Truncate(string? value) =>
        value is null || value.Length <= maxPayloadLength ? value : value[..maxPayloadLength];

    private static string FormatHeaders(System.Net.Http.Headers.HttpHeaders headers) =>
        string.Join("; ", headers.Select(h =>
            $"{h.Key}: {(RedactedHeaders.Contains(h.Key) ? "***" : string.Join(",", h.Value))}"));
}
