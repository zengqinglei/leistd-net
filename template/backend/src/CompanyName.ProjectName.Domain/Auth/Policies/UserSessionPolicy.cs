#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Policies;

/// <summary>会话所属租户当前生效的时长策略。</summary>
public sealed record UserSessionPolicy(
    bool AllowKeepSignedIn,
    TimeSpan SessionLifetime,
    TimeSpan KeepSignedInLifetime,
    TimeSpan IdleTimeout)
{
    /// <summary>按原认证时刻计算绝对到期。</summary>
    public DateTime AbsoluteExpiry(DateTime authenticatedAt, bool keepSignedIn) =>
        authenticatedAt + (keepSignedIn && AllowKeepSignedIn ? KeepSignedInLifetime : SessionLifetime);

    /// <summary>按会话档位取绝对与空闲截止中先到的一项。</summary>
    public DateTime Expiry(DateTime authenticatedAt, bool keepSignedIn, DateTime lastSeenTime)
    {
        var absolute = AbsoluteExpiry(authenticatedAt, keepSignedIn);
        var idle = lastSeenTime + IdleTimeout;
        return keepSignedIn && AllowKeepSignedIn && idle < absolute ? idle : absolute;
    }
}
#endif
