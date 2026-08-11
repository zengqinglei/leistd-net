using System.Net;
using System.Net.Http.Headers;
using Leistd.ServiceClient.OAuth.Services;

namespace Leistd.ServiceClient.OAuth.Handlers;

/// <summary>
/// client credentials 认证处理器（管道最内层）：为出站请求附加 Bearer 令牌；
/// 收到 401 时使缓存失效、强制重取令牌并重试一次（对上层透明）。
/// 请求已自带 <c>Authorization</c> 头时不介入（也不做 401 重试）。
/// </summary>
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

        // 为可能的 401 重试预先缓冲请求体（缓冲后可重复读取）
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

        // 401 自愈：令牌可能已被吊销或密钥轮换，强刷一次
        tokenProvider.Invalidate(clientName);
        var freshToken = await tokenProvider.GetAccessTokenAsync(clientName, cancellationToken);

        var retryRequest = await CloneRequestAsync(request, cancellationToken);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        response.Dispose();

        return await base.SendAsync(retryRequest, cancellationToken);
    }

    /// <summary>
    /// 克隆请求用于重试（同一 <see cref="HttpRequestMessage"/> 不允许发送两次）。
    /// 请求体已在首次发送前缓冲，可安全复制。
    /// </summary>
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
