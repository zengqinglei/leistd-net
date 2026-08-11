using System.Text.Json;
using Leistd.Response.Core.Wrapper;
using Leistd.ServiceClient.Exceptions;

namespace Leistd.ServiceClient.Http;

/// <summary>
/// 服务调用响应读取扩展：统一响应（<c>Result&lt;T&gt;</c>）解包与远端错误还原。
/// </summary>
public static class HttpResponseMessageExtensions
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 异常消息与 <see cref="RemoteServiceException.ResponseBody"/> 中原始响应体的最大保留长度。
    /// </summary>
    private const int MaxBodySnippetLength = 4096;

    /// <summary>
    /// 读取统一响应并返回 <c>data</c>。非 2xx 或 <c>code != 0</c> 抛 <see cref="RemoteServiceException"/>；
    /// 响应体为空或不是有效 JSON 抛 <see cref="ServiceClientException"/>。
    /// </summary>
    /// <typeparam name="T">统一响应 <c>data</c> 的类型</typeparam>
    /// <param name="response">HTTP 响应</param>
    /// <param name="jsonOptions">自定义序列化选项，默认 camelCase 且大小写不敏感（<see cref="JsonSerializerDefaults.Web"/>）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<T?> ReadResultAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureRemoteSuccessAsync(response, cancellationToken);

        var envelope = await DeserializeAsync<Result<T>>(response, jsonOptions, cancellationToken);
        EnsureEnvelopeSuccess(response, envelope);
        return envelope.Data;
    }

    /// <summary>
    /// 读取无数据负载的统一响应（<c>Result</c>）。非 2xx 或 <c>code != 0</c> 抛 <see cref="RemoteServiceException"/>。
    /// </summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="jsonOptions">自定义序列化选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task ReadResultAsync(
        this HttpResponseMessage response,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureRemoteSuccessAsync(response, cancellationToken);

        var envelope = await DeserializeAsync<Result>(response, jsonOptions, cancellationToken);
        EnsureEnvelopeSuccess(response, envelope);
    }

    /// <summary>
    /// 读取未经统一响应包装的内容（远端 <c>[NoWrap]</c> 端点）并直接反序列化为 <typeparamref name="T"/>。
    /// 非 2xx 抛 <see cref="RemoteServiceException"/>。文件流场景请直接使用
    /// <see cref="HttpResponseMessage.Content"/>，先调用 <see cref="EnsureRemoteSuccessAsync"/> 检查错误。
    /// </summary>
    /// <typeparam name="T">响应内容类型</typeparam>
    /// <param name="response">HTTP 响应</param>
    /// <param name="jsonOptions">自定义序列化选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<T?> ReadContentAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureRemoteSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, jsonOptions, cancellationToken);
    }

    /// <summary>
    /// 确认远端返回 2xx；否则解析 ProblemDetails（<c>code</c> / <c>message</c> / <c>traceId</c> / <c>errors</c>）
    /// 并抛出 <see cref="RemoteServiceException"/>。响应体不是 ProblemDetails 时附原始体（截断）。
    /// </summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task EnsureRemoteSuccessAsync(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (System.Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RemoteServiceException(
                $"{Describe(response)} 远端返回错误，且响应体读取失败: {ex.Message}",
                (int)response.StatusCode,
                innerException: ex);
        }

        var snippet = Truncate(body);
        int? errorCode = null;
        string? message = null;
        string? traceId = null;
        List<RemoteErrorItem>? errors = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    errorCode = TryGetInt(root, "code");
                    message = TryGetString(root, "message") ?? TryGetString(root, "detail") ?? TryGetString(root, "title");
                    traceId = TryGetString(root, "traceId");
                    errors = TryGetErrors(root);
                }
            }
            catch (JsonException)
            {
                // 非 JSON 错误体（如网关 HTML）：保留原始体片段即可
            }
        }

        throw new RemoteServiceException(
            $"{Describe(response)} 远端返回错误: {message ?? snippet}" +
            (traceId is null ? string.Empty : $" (远端 traceId: {traceId})"),
            (int)response.StatusCode,
            errorCode,
            traceId,
            errors,
            snippet);
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions? jsonOptions,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ServiceClientException($"{Describe(response)} 响应体为空");
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(body, jsonOptions ?? DefaultJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ServiceClientException($"{Describe(response)} 响应反序列化失败: {ex.Message}", ex);
        }

        return value ?? throw new ServiceClientException($"{Describe(response)} 响应体为空");
    }

    private static void EnsureEnvelopeSuccess(HttpResponseMessage response, Result envelope)
    {
        if (envelope.Code == 0)
        {
            return;
        }

        throw new RemoteServiceException(
            $"{Describe(response)} 远端业务失败: code={envelope.Code} {envelope.Message}",
            (int)response.StatusCode,
            envelope.Code);
    }

    private static string Describe(HttpResponseMessage response)
    {
        var request = response.RequestMessage;
        return $"{request?.Method} {(int)response.StatusCode} {request?.RequestUri}";
    }

    private static string Truncate(string value) =>
        value.Length <= MaxBodySnippetLength ? value : value[..MaxBodySnippetLength];

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value)
            ? value
            : null;

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static List<RemoteErrorItem>? TryGetErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errorsElement) || errorsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<RemoteErrorItem>();
        foreach (var item in errorsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new RemoteErrorItem(
                TryGetString(item, "field"),
                TryGetString(item, "detail"),
                TryGetString(item, "code")));
        }

        return items;
    }
}
