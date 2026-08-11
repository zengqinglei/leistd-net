using Leistd.Security.Users;
using Leistd.ServiceClient.Constants;
using Leistd.ServiceClient.Options;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 用户上下文出站转发处理器：把当前用户（<see cref="ICurrentUser"/>）的 Id、用户名及
/// 配置映射的 claim 写入出站请求头，供被调方在受信前提下恢复用户主体。
/// 仅在请求尚无同名头时追加；当前无认证用户时不做任何处理。
/// </summary>
/// <param name="currentUser">当前用户（读取时机为每次发送，而非构造时）</param>
/// <param name="options">转发配置</param>
public sealed class UserContextDelegatingHandler(ICurrentUser currentUser, UserContextForwardingOptions options)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!options.Enable || !currentUser.IsAuthenticated)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (currentUser.Id is { } userId)
        {
            AddHeaderIfAbsent(request, ServiceClientHeaders.UserId, userId.ToString());
        }

        if (options.ForwardUserName && !string.IsNullOrEmpty(currentUser.Username))
        {
            AddHeaderIfAbsent(request, ServiceClientHeaders.UserName, Uri.EscapeDataString(currentUser.Username));
        }

        foreach (var (claimType, headerName) in options.ClaimHeaderMap)
        {
            var claim = currentUser.FindClaim(claimType);
            if (!string.IsNullOrEmpty(claim?.Value))
            {
                AddHeaderIfAbsent(request, headerName, Uri.EscapeDataString(claim.Value));
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static void AddHeaderIfAbsent(HttpRequestMessage request, string name, string value)
    {
        if (!request.Headers.Contains(name))
        {
            request.Headers.Add(name, value);
        }
    }
}
