using System.Collections.Concurrent;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// 基于 SignalR 连接跟踪在线状态的 <see cref="IPresenceService"/> 实现（单机内存）。
/// </summary>
/// <remarks>
/// 使用 userId -> 连接数 的计数，正确处理同一用户多连接：仅当某用户所有连接断开时才判为离线。
/// 分布式（多实例）场景需 Redis Backplane，当前为预留。
/// </remarks>
public class SignalRPresenceService : IPresenceService
{
    private static readonly ConcurrentDictionary<string, int> OnlineUsers = new();

    /// <inheritdoc/>
    public Task<bool> IsOnlineAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(OnlineUsers.TryGetValue(userId, out var count) && count > 0);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetOnlineUserIdsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(OnlineUsers.Keys.ToList());

    /// <summary>标记用户一条连接上线（由 Hub OnConnectedAsync 调用）。</summary>
    internal static void UserConnected(string userId)
        => OnlineUsers.AddOrUpdate(userId, 1, (_, count) => count + 1);

    /// <summary>标记用户一条连接下线（由 Hub OnDisconnectedAsync 调用）。</summary>
    internal static void UserDisconnected(string userId)
    {
        OnlineUsers.AddOrUpdate(userId, 0, (_, count) => count - 1);
        // 计数归零则移除，避免泄漏
        if (OnlineUsers.TryGetValue(userId, out var c) && c <= 0)
        {
            OnlineUsers.TryRemove(userId, out _);
        }
    }
}
