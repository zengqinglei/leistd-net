using System.Diagnostics;
using Leistd.ServiceClient.Exceptions;
using Microsoft.Extensions.Logging;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 服务调用日志处理器（管道最外层）：每次调用输出一行结构化摘要
/// （服务名、方法、URI、状态码、耗时），可选 Debug 级别载荷日志（脱敏、截断）。
/// 同时把传输层异常包装为 <see cref="ServiceClientException"/>（调用方主动取消除外）。
/// TraceId 由链路追踪组件的日志 Scope 附着，此处不重复打印。
/// </summary>
public sealed class ServiceClientLoggingHandler(
    ILogger logger,
    string serviceName,
    bool logPayloads,
    int maxPayloadLength) : DelegatingHandler
{
    /// <summary>
    /// 载荷日志中值被脱敏的请求/响应头（不区分大小写）。
    /// </summary>
    private static readonly HashSet<string> RedactedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        Constants.ServiceClientHeaders.UserId,
        Constants.ServiceClientHeaders.UserName,
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
                "{Service} {Method} {Uri} 请求头 {Headers} 请求体 {Body}",
                serviceName, request.Method, request.RequestUri,
                FormatHeaders(request.Headers), Truncate(requestBody));
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (System.Exception ex)
        {
            var failedElapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogError(
                ex,
                "{Service} {Method} {Uri} 调用失败，耗时 {ElapsedMs}ms",
                serviceName, request.Method, request.RequestUri, (long)failedElapsed.TotalMilliseconds);

            // 调用方主动取消与 SDK 自身异常原样上抛；传输层异常统一包装
            if (ex is ServiceClientException ||
                (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                throw;
            }

            throw new ServiceClientException(
                $"{request.Method} {request.RequestUri} 调用失败: {ex.Message}", ex);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        logger.Log(
            response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning,
            "{Service} {Method} {Uri} 响应 {StatusCode}，耗时 {ElapsedMs}ms",
            serviceName, request.Method, request.RequestUri,
            (int)response.StatusCode, (long)elapsed.TotalMilliseconds);

        if (logPayloads && logger.IsEnabled(LogLevel.Debug) && response.Content is not null)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogDebug(
                "{Service} {Method} {Uri} 响应体 {Body}",
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
