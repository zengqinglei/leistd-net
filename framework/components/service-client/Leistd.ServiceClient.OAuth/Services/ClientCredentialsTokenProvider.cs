using System.Collections.Concurrent;
using System.Text.Json;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.OAuth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.OAuth.Services;

/// <summary>
/// 基于 OAuth2 client credentials 流的令牌提供者：
/// 向身份服务 token 端点（默认 <c>/connect/token</c>）以 <c>grant_type=client_credentials</c>
/// 换取访问令牌，按具名客户端缓存至过期缓冲，并用信号量单飞防止并发重复获取。
/// </summary>
/// <param name="httpClientFactory">HttpClient 工厂（token 请求使用独立客户端 <see cref="TokenHttpClientName"/>，避免管道递归）</param>
/// <param name="optionsMonitor">具名认证配置</param>
/// <param name="logger">日志</param>
public class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClientCredentialsOptions> optionsMonitor,
    ILogger<ClientCredentialsTokenProvider> logger) : IServiceTokenProvider
{
    /// <summary>
    /// token 请求专用 HttpClient 名（无认证与用户头处理器，避免管道递归）。
    /// </summary>
    public const string TokenHttpClientName = "Leistd.ServiceClient.OAuth.Token";

    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(string clientName, CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.Get(clientName);
        if (_cache.TryGetValue(clientName, out var cached) && !cached.IsExpired(options.ExpirationBuffer))
        {
            return cached.AccessToken;
        }

        var gate = _gates.GetOrAdd(clientName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 双检：等待期间可能已有并发请求完成获取
            if (_cache.TryGetValue(clientName, out cached) && !cached.IsExpired(options.ExpirationBuffer))
            {
                return cached.AccessToken;
            }

            var token = await RequestTokenAsync(clientName, options, cancellationToken);
            _cache[clientName] = token;
            return token.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate(string clientName) => _cache.TryRemove(clientName, out _);

    private async Task<CachedToken> RequestTokenAsync(
        string clientName, ClientCredentialsOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new ServiceClientException($"服务客户端 {clientName} 未配置 ClientId，无法获取访问令牌。");
        }

        string tokenEndpoint;
        try
        {
            tokenEndpoint = options.ResolveTokenEndpoint();
        }
        catch (InvalidOperationException ex)
        {
            throw new ServiceClientException($"服务客户端 {clientName} 认证配置无效: {ex.Message}", ex);
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
        };
        if (!string.IsNullOrWhiteSpace(options.Scope))
        {
            form["scope"] = options.Scope;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        var httpClient = httpClientFactory.CreateClient(TokenHttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (System.Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceClientException(
                $"服务客户端 {clientName} 获取访问令牌失败（{tokenEndpoint} 不可达）: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServiceClientException(
                    $"服务客户端 {clientName} 获取访问令牌失败: {(int)response.StatusCode} {tokenEndpoint} {Truncate(body)}");
            }

            return ParseToken(clientName, body);
        }
    }

    private CachedToken ParseToken(string clientName, string body)
    {
        string? accessToken = null;
        var expiresIn = 3600d;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("access_token", out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String)
            {
                accessToken = tokenElement.GetString();
            }

            if (root.TryGetProperty("expires_in", out var expiresElement) &&
                expiresElement.ValueKind == JsonValueKind.Number)
            {
                expiresIn = expiresElement.GetDouble();
            }
        }
        catch (JsonException ex)
        {
            throw new ServiceClientException(
                $"服务客户端 {clientName} 的令牌响应不是有效 JSON: {ex.Message}", ex);
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new ServiceClientException($"服务客户端 {clientName} 的令牌响应缺少 access_token。");
        }

        logger.LogDebug("服务客户端 {ClientName} 已获取访问令牌，有效期 {ExpiresIn}s", clientName, expiresIn);
        return new CachedToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static string Truncate(string value) => value.Length <= 2048 ? value : value[..2048];

    // 缓存中的访问令牌（AccessToken + 过期时刻 UTC）。仅本提供者使用——它不出现在
    // IServiceTokenProvider 的任何签名里，使用者既不构造也不接收它，因此不作为公共类型暴露。
    // 用普通注释而非 XML 注释：后者会被编译进随包分发的 .xml 文档，让私有类型出现在公共文档产物里。
    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt)
    {
        // 是否已过期（含提前刷新缓冲）
        public bool IsExpired(TimeSpan buffer) => DateTimeOffset.UtcNow >= ExpiresAt - buffer;
    }
}
