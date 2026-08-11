namespace Leistd.ServiceClient.OAuth.Models;

/// <summary>
/// 已获取的服务访问令牌。
/// </summary>
/// <param name="AccessToken">访问令牌</param>
/// <param name="ExpiresAt">过期时刻（UTC）</param>
public sealed record ServiceToken(string AccessToken, DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// 是否已过期（含提前刷新缓冲）。
    /// </summary>
    /// <param name="buffer">过期缓冲</param>
    public bool IsExpired(TimeSpan buffer) => DateTimeOffset.UtcNow >= ExpiresAt - buffer;
}
