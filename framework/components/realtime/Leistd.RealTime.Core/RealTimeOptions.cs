namespace Leistd.RealTime;

/// <summary>
/// 实时（RealTime）传输配置。
/// </summary>
public class RealTimeOptions
{
    /// <summary>业务事件 Hub 路径。</summary>
    public string RealTimeHubPath { get; set; } = "/hubs/realtime";

    /// <summary>心跳间隔（默认 15 秒）。</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>客户端超时（默认 30 秒）。</summary>
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>是否启用详细错误（开发环境）。</summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// 用于解析 SignalR UserIdentifier 的 claim 类型（按顺序取第一个非空）。
    /// 默认兼容 OpenIddict/OAuth2 的 "sub" 及标准 nameidentifier。
    /// </summary>
    public IReadOnlyList<string> UserIdClaimTypes { get; set; } =
        ["sub", System.Security.Claims.ClaimTypes.NameIdentifier];

    /// <summary>
    /// 是否启用 Redis Backplane（多实例扩展）。当前为预留，未实现。
    /// </summary>
    public bool EnableRedisBackplane { get; set; }

    /// <summary>Redis 连接字符串（启用 Backplane 时使用）。预留。</summary>
    public string? RedisConnectionString { get; set; }
}
