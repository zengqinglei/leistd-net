using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// 按 <see cref="RealTimeOptions.UserIdClaimTypes"/> 配置的 claim 顺序解析 SignalR UserIdentifier。
/// </summary>
/// <remarks>
/// 默认 IUserIdProvider 使用 ClaimTypes.NameIdentifier，但 OpenIddict/OAuth2 标准用 "sub"。
/// 这里改为可配置，避免硬编码。
/// </remarks>
public class ClaimUserIdProvider(IOptions<RealTimeOptions> options) : IUserIdProvider
{
    private readonly RealTimeOptions _options = options.Value;

    public string? GetUserId(HubConnectionContext connection)
    {
        foreach (var claimType in _options.UserIdClaimTypes)
        {
            var value = connection.User?.FindFirst(claimType)?.Value;
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        return null;
    }
}
