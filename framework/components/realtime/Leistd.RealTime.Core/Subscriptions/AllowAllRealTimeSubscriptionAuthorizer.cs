using Leistd.RealTime.Publishing;
using Leistd.RealTime.Subscriptions;

namespace Leistd.RealTime.Subscriptions;

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
