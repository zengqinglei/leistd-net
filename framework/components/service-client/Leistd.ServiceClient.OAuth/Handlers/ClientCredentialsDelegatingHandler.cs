using System.Net;
using System.Net.Http.Headers;
using Leistd.ServiceClient.OAuth.Services;
using Leistd.ServiceClient.OAuth.Abstractions;

namespace Leistd.ServiceClient.OAuth.Handlers;

/// <summary>
/// 为出站请求附加 client credentials 访问令牌。
/// </summary>
/// <remarks>收到 401 时刷新令牌并重试一次；已有 Authorization 头时不介入。</remarks>
/// <param name="clientName">具名客户端名（对应认证配置节）</param>
/// <param name="tokenProvider">令牌提供者</param>
public sealed class ClientCredentialsDelegatingHandler(
    string clientName,
    IServiceTokenProvider tokenProvider) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is not null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // 请求体必须预先缓冲，401 重试时才能重新发送。
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        var token = await tokenProvider.GetAccessTokenAsync(clientName, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // 401 可能来自令牌吊销或密钥轮换，强制刷新一次。
        tokenProvider.Invalidate(clientName);
        var freshToken = await tokenProvider.GetAccessTokenAsync(clientName, cancellationToken);

        var retryRequest = await CloneRequestAsync(request, cancellationToken);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        response.Dispose();

        return await base.SendAsync(retryRequest, cancellationToken);
    }

    // HttpRequestMessage 不能发送两次，重试必须复制请求。
    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
}
