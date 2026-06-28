using Leistd.Security.Users;

namespace Leistd.RealTime;

/// <summary>
/// 实时资源订阅授权器。
/// </summary>
public interface IRealtimeSubscriptionAuthorizer
{
    /// <summary>
    /// 判断当前用户是否允许订阅指定资源。
    /// </summary>
    Task<bool> AuthorizeAsync(
        RealtimeSubscriptionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 实时资源订阅上下文。
/// </summary>
public sealed record RealtimeSubscriptionContext(
    string ResourceKey,
    ICurrentUser CurrentUser);

/// <summary>
/// 默认订阅授权器。默认允许订阅，避免影响公共资源订阅场景。
/// </summary>
public sealed class AllowAllRealtimeSubscriptionAuthorizer : IRealtimeSubscriptionAuthorizer
{
    public Task<bool> AuthorizeAsync(
        RealtimeSubscriptionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
