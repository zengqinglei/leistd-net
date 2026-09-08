using System.Collections.Concurrent;
using System.Text.Json;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.OAuth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.ServiceClient.OAuth.Abstractions;

namespace Leistd.ServiceClient.OAuth.Services;

/// <summary>
/// 获取并缓存 client credentials 访问令牌。
/// </summary>
/// <remarks>令牌按客户端隔离，并通过单飞控制避免并发重复获取。</remarks>
/// <param name="httpClientFactory">HttpClient 工厂（token 请求使用独立客户端 <see cref="TokenHttpClientName"/>，避免管道递归）</param>
/// <param name="optionsMonitor">具名认证配置</param>
/// <param name="logger">日志</param>
/// <param name="timeProvider">时间源；省略时使用 <see cref="TimeProvider.System"/>。</param>
public class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClientCredentialsOptions> optionsMonitor,
    ILogger<ClientCredentialsTokenProvider> logger,
    TimeProvider? timeProvider = null) : IServiceTokenProvider
{
    // 时间源可注入：令牌缓存的过期判定与「提前 ExpirationBuffer 刷新」都靠它，
    // 写死 DateTimeOffset.UtcNow 的话这两条只能靠真的等到过期才能验证。
    // 默认落 TimeProvider.System，宿主无需为此多配一项。
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
        if (_cache.TryGetValue(clientName, out var cached) &&
            !cached.IsExpired(_timeProvider.GetUtcNow(), options.ExpirationBuffer))
        {
            return cached.AccessToken;
        }

        var gate = _gates.GetOrAdd(clientName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 等待期间其他请求可能已刷新缓存，进入临界区后必须再次检查。
            if (_cache.TryGetValue(clientName, out cached) &&
                !cached.IsExpired(_timeProvider.GetUtcNow(), options.ExpirationBuffer))
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
            throw new ServiceClientException($"Service client {clientName} has no ClientId configured; cannot obtain an access token.");
        }

        string tokenEndpoint;
        try
        {
            tokenEndpoint = options.ResolveTokenEndpoint();
        }
        catch (InvalidOperationException ex)
        {
            throw new ServiceClientException($"Service client {clientName} has invalid authentication configuration: {ex.Message}", ex);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceClientException(
                $"Service client {clientName} failed to obtain an access token ({tokenEndpoint} unreachable): {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServiceClientException(
                    $"Service client {clientName} failed to obtain an access token: {(int)response.StatusCode} {tokenEndpoint} {Truncate(body)}");
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
                $"Token response for service client {clientName} is not valid JSON: {ex.Message}", ex);
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new ServiceClientException($"Token response for service client {clientName} is missing access_token.");
        }

        logger.LogDebug("Service client {ClientName} obtained an access token; expires in {ExpiresIn}s", clientName, expiresIn);
        return new CachedToken(accessToken, _timeProvider.GetUtcNow().AddSeconds(expiresIn));
    }

    private static string Truncate(string value) => value.Length <= 2048 ? value : value[..2048];

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired(DateTimeOffset now, TimeSpan buffer) => now >= ExpiresAt - buffer;
    }
}
