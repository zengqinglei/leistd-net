#if (LocalIdentity)
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace CompanyName.ProjectName.Application.Auth.TwoFactor;

/// <summary>
/// 登录第二步的挑战：密码（或外部登录）已通过、尚待验证码的那一段。
/// </summary>
/// <remarks>
/// <para>凭据放在缓存里、只把随机令牌交给浏览器：这一段还不是会话，不能发 Cookie——
/// 发了就等于第一步即登录成功，第二步形同虚设。</para>
/// <para>同一挑战最多试 <see cref="MaxAttempts"/> 次，用完须重新输入密码；
/// 每次输错同时计入账号的登录失败次数，所以换挑战重试也绕不过锁定。</para>
/// </remarks>
internal sealed class TwoFactorChallengeStore(IDistributedCache cache)
{
    /// <summary>挑战的有效期。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>单个挑战允许的错误次数。</summary>
    public const int MaxAttempts = 5;

    private const string KeyPrefix = "auth:2fa-login:";

    public async Task<string> CreateAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        await SaveAsync(token, new TwoFactorChallenge(userId, tenantId, 0), cancellationToken);
        return token;
    }

    public async Task<TwoFactorChallenge?> GetAsync(string token, CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(Key(token), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<TwoFactorChallenge>(json);
    }

    /// <summary>记一次输错；次数用完时作废挑战并返回 false。</summary>
    public async Task<bool> RecordFailureAsync(string token, TwoFactorChallenge challenge, CancellationToken cancellationToken)
    {
        var attempts = challenge.Attempts + 1;
        if (attempts >= MaxAttempts)
        {
            await RemoveAsync(token, cancellationToken);
            return false;
        }

        await SaveAsync(token, challenge with { Attempts = attempts }, cancellationToken);
        return true;
    }

    public Task RemoveAsync(string token, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(token), cancellationToken);

    // 输错时重写会把有效期重新算满；次数有上限，总时长也就有上界，不必另记原始到期时刻
    private Task SaveAsync(string token, TwoFactorChallenge challenge, CancellationToken cancellationToken) =>
        cache.SetStringAsync(
            Key(token),
            JsonSerializer.Serialize(challenge),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime },
            cancellationToken);

    // 键里不放令牌原文：缓存是另一套存储，能读到键名的人不该因此拿到可用的令牌
    private static string Key(string token) =>
        KeyPrefix + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

}

/// <summary>一个待完成的第二步。</summary>
/// <param name="UserId">第一步已认出的用户。</param>
/// <param name="TenantId">第一步所在的租户；第二步必须在同一租户下完成。</param>
/// <param name="Attempts">已输错的次数。</param>
internal sealed record TwoFactorChallenge(Guid UserId, Guid? TenantId, int Attempts);
#endif
