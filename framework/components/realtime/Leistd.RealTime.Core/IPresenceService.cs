namespace Leistd.RealTime;

/// <summary>
/// 在线状态服务：跟踪用户实时连接状态。
/// </summary>
public interface IPresenceService
{
    /// <summary>检查用户是否在线。</summary>
    Task<bool> IsOnlineAsync(string userId, CancellationToken ct = default);

    /// <summary>获取所有在线用户 ID。</summary>
    Task<IReadOnlyList<string>> GetOnlineUserIdsAsync(CancellationToken ct = default);
}
