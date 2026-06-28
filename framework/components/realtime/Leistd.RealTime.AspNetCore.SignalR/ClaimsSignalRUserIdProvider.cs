using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Leistd.RealTime;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// 在 SignalR 连接边界按配置的 claim 顺序解析 UserIdentifier。
/// </summary>
/// <remarks>
/// 这里故意读取 <see cref="HubConnectionContext.User"/>：IUserIdProvider 是 SignalR 基础设施边界，
/// 业务代码仍应通过 ICurrentUser 获取当前用户。
/// </remarks>
public class ClaimsSignalRUserIdProvider(IOptions<RealTimeOptions> options) : IUserIdProvider
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
