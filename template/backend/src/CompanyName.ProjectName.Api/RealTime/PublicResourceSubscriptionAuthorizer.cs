#if (IncludeNotifications)
using Leistd.RealTime.Abstractions;

namespace CompanyName.ProjectName.Api.RealTime;

/// <summary>
/// 只放行公共命名空间（<c>public:</c> 前缀）的实时资源订阅。
/// </summary>
/// <remarks>
/// 组名不含租户段，`Subscribe` 收的是客户端给的任意字符串——放行任意 key 等于允许
/// 已认证用户订阅别的租户的资源。默认因此收窄到显式公共的命名空间。
/// 需要订阅租户内资源时在这里加判定：本方法在 Hub 调用内执行，可直接注入
/// <c>ICurrentTenant</c> / <c>ICurrentUser</c> 读环境态。
/// </remarks>
public sealed class PublicResourceSubscriptionAuthorizer : IRealTimeSubscriptionAuthorizer
{
    /// <summary>公共资源命名空间前缀。</summary>
    public const string PublicPrefix = "public:";

    /// <inheritdoc />
    public Task<bool> AuthorizeAsync(
        RealTimeSubscriptionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(context.ResourceKey.StartsWith(PublicPrefix, StringComparison.Ordinal));
    }
}
#endif
