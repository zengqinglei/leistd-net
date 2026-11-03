using System.Net.Http.Headers;
using System.Text;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.Delivery;

// 默认发送器：签名、POST、如实报告一次尝试。重定向不跟随（3xx 原样作为失败结果），超时与连接失败返回失败结果，
// 只有调用方取消才抛出。
internal sealed class WebhookHttpSender(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<WebhookOptions> options,
    WebhookSigner signer,
    TimeProvider timeProvider) : IWebhookSender
{
    internal const string HttpClientName = "Leistd.Webhooks";

    public async Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = CreateRequestMessage(request);
        var requestHeaders = SnapshotHeaders(message);
        var timeout = options.CurrentValue.RequestTimeout;
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var startedAt = timeProvider.GetTimestamp();

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure($"Request timed out after {timeout}.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(DescribeFailure(exception));
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;
            var retryAfter = GetRetryAfter(response.Headers.RetryAfter);
            string? body = null;
            try
            {
                body = await ReadTruncatedAsync(response.Content, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // 已经收到状态码：响应体读不完不改变结果，只是不记录正文。
            }

            return new WebhookSendResult
            {
                StatusCode = statusCode,
                Duration = timeProvider.GetElapsedTime(startedAt),
                RequestHeaders = requestHeaders,
                Response = body,
                RetryAfter = retryAfter
            };
        }

        WebhookSendResult Failure(string error) => new()
        {
            Duration = timeProvider.GetElapsedTime(startedAt),
            RequestHeaders = requestHeaders,
            Error = error
        };
    }

    private HttpRequestMessage CreateRequestMessage(WebhookSendRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json")
        };

        foreach (var (name, value) in request.Headers)
        {
            // 内容头（如 Content-Type）不能写进请求头集合，TryAdd 返回 false 即忽略；组件头随后覆盖同名自定义头。
            message.Headers.TryAddWithoutValidation(name, value);
        }

        var id = request.EventId.ToString();
        SetHeader(message, WebhookSigner.IdHeader, id);
        SetHeader(message, WebhookSigner.TimestampHeader, WebhookSigner.FormatTimestamp(request.Timestamp));
        SetHeader(message, WebhookSigner.SignatureHeader, signer.Sign(id, request.Timestamp, request.Body, request.Secrets));
        return message;
    }

    private static void SetHeader(HttpRequestMessage message, string name, string value)
    {
        message.Headers.Remove(name);
        message.Headers.TryAddWithoutValidation(name, value);
    }

    private static Dictionary<string, string> SnapshotHeaders(HttpRequestMessage message)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in message.Headers.NonValidated)
        {
            if (!string.Equals(name, WebhookSigner.SignatureHeader, StringComparison.OrdinalIgnoreCase))
            {
                snapshot[name] = values.ToString();
            }
        }

        if (message.Content is not null)
        {
            foreach (var (name, values) in message.Content.Headers.NonValidated)
            {
                snapshot[name] = values.ToString();
            }
        }

        return snapshot;
    }

    private TimeSpan? GetRetryAfter(RetryConditionHeaderValue? header)
    {
        var wait = header switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - timeProvider.GetUtcNow(),
            _ => (TimeSpan?)null
        };

        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    // 只读前 MaxResponseLength 个字符，不把整段响应读进内存。
    private static async Task<string> ReadTruncatedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, GetEncoding(content.Headers.ContentType), detectEncodingFromByteOrderMarks: true);
            var buffer = new char[WebhookSendResult.MaxResponseLength];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            return new string(buffer, 0, read);
        }
    }

    private static Encoding GetEncoding(MediaTypeHeaderValue? contentType)
    {
        if (contentType?.CharSet is { Length: > 0 } charSet)
        {
            try
            {
                return Encoding.GetEncoding(charSet.Trim('"'));
            }
            catch (ArgumentException)
            {
                // 未知字符集按 UTF-8 读，记录只用于排查。
            }
        }

        return Encoding.UTF8;
    }

    // HttpClient 把连接回调里的异常包一层；内层消息才写明被拦截的地址与放行配置键。
    private static string DescribeFailure(HttpRequestException exception)
    {
        var root = exception.GetBaseException();
        return ReferenceEquals(root, exception) ? exception.Message : $"{exception.Message} {root.Message}";
    }
}
