using Leistd.Security.Users;
using Leistd.ServiceClient.Constants;
using Leistd.ServiceClient.Options;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 将当前用户上下文写入出站请求头。
/// </summary>
/// <remarks>仅转发已认证用户，且不覆盖请求已有的同名头。</remarks>
/// <param name="currentUser">当前用户（读取时机为每次发送，而非构造时）</param>
/// <param name="optionsMonitor">服务客户端配置监视器</param>
/// <typeparam name="TOptions">当前服务客户端选项类型。</typeparam>
public sealed class UserContextDelegatingHandler<TOptions>(
    ICurrentUser currentUser,
    IOptionsMonitor<TOptions> optionsMonitor)
    : DelegatingHandler
    where TOptions : ServiceClientOptions
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue.UserContext;
        if (!options.Enabled || !currentUser.IsAuthenticated)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (currentUser.Id is { } userId)
        {
            AddHeaderIfAbsent(request, ServiceClientHeaders.UserId, userId.ToString());
        }

        if (options.ForwardUsername && !string.IsNullOrEmpty(currentUser.Username))
        {
            AddHeaderIfAbsent(request, ServiceClientHeaders.Username, Uri.EscapeDataString(currentUser.Username));
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
