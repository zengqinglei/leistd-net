namespace Leistd.RealTime.Abstractions;

/// <summary>
/// 实时资源订阅上下文。
/// </summary>
/// <param name="ResourceKey">资源标识（如 "product-profile:{id}"）。</param>
/// <param name="UserId">当前连接用户标识；匿名连接为 null。</param>
public sealed record RealTimeSubscriptionContext(
    string ResourceKey,
    string? UserId);
