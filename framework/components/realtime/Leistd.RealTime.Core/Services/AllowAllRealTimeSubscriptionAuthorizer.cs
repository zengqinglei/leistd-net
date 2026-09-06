using Leistd.RealTime.Abstractions;

namespace Leistd.RealTime.Services;

/// <summary>
/// 默认允许所有订阅的授权器。
/// </summary>
public sealed class AllowAllRealTimeSubscriptionAuthorizer : IRealTimeSubscriptionAuthorizer
{
    /// <inheritdoc />
    public Task<bool> AuthorizeAsync(
        RealTimeSubscriptionContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
