#if (LocalIdentity)
using System.Linq.Expressions;
using CompanyName.ProjectName.Domain.Auth.Policies;
using CompanyName.ProjectName.Domain.Auth.Events;
using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Domain.Entities.Auditing;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Auth.Entities;

/// <summary>一次登录建立的会话（"登录设备"里的一行）。</summary>
/// <remarks>
/// <para>会话 Cookie 本身是自包含的：服务端不登记就无从得知谁在哪儿登录着，更撤不回一个已发出的 Cookie。
/// 登记后 Cookie 只携带会话 Id（<c>sid</c> 声明），每次校验都要这一行还在——
/// <b>撤销即删除</b>，不留"已撤销"状态：撤销的历史由操作记录承载，这张表只回答"现在有哪些会话有效"。</para>
/// <para>实现 <see cref="IMultiTenant"/>：会话随用户落在其租户的库里，分库租户也不例外。</para>
/// </remarks>
public class UserSession : CreationAuditedEntity<Guid>, IAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>IP 地址列宽（IPv6 文本形式的最大长度）。</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>User-Agent 列宽；超出部分截断，只用于辨认设备。</summary>
    public const int UserAgentMaxLength = 512;

    /// <summary>模拟发起人名称的列宽。</summary>
    public const int ImpersonatorNameMaxLength = 128;

    /// <summary>最近活跃时间的更新间隔：同一会话在这个间隔内的请求不再写库。</summary>
    /// <remarks>每个请求都写一次的话，读多写少的接口全都变成了写请求。</remarks>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    /// <summary>所属租户（null 为宿主），由多租户落值拦截器在创建时填充。</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>会话所属用户。</summary>
    public Guid UserId { get; private set; }

    /// <summary>本次原始登录是否选择保持登录。</summary>
    public bool KeepSignedIn { get; private set; }

    /// <summary>本次原始认证时刻，模拟登录换发时沿用。</summary>
    public DateTime AuthenticatedAt { get; private set; }

    /// <summary>最近一次经校验的请求时间（按 <see cref="TouchInterval"/> 节流）。</summary>
    public DateTime LastSeenTime { get; private set; }

    /// <summary>最近一次请求的客户端 IP。</summary>
    public string? IpAddress { get; private set; }

    /// <summary>登录时的 User-Agent。</summary>
    public string? UserAgent { get; private set; }

    /// <summary>模拟登录建立的会话：发起人的名称；普通登录为 null。</summary>
    /// <remarks>被模拟的账号在自己的设备列表里能看到"谁以我的身份进来过"，与操作记录的信任属性一致。</remarks>
    public string? ImpersonatorName { get; private set; }

    private UserSession()
    {
    }

    public UserSession(Guid userId, DateTime now, string? ipAddress, string? userAgent, string? impersonatorName = null,
        bool keepSignedIn = false, DateTime? authenticatedAt = null)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        AuthenticatedAt = authenticatedAt ?? now;
        KeepSignedIn = keepSignedIn;
        LastSeenTime = now;
        IpAddress = Truncate(ipAddress, IpAddressMaxLength);
        UserAgent = Truncate(userAgent, UserAgentMaxLength);
        ImpersonatorName = Truncate(impersonatorName, ImpersonatorNameMaxLength);
    }

    /// <summary>撤销：调用方随后删除这一行。发出 <see cref="UserSessionRevokedEvent"/>，事务提交后订阅方据此作废会话校验缓存。</summary>
    /// <param name="now">撤销时刻。</param>
    public void Revoke(DateTime now) => AddLocalEvent(new UserSessionRevokedEvent(Id, now));

    /// <summary>按当前策略判定会话是否已过期。</summary>
    public bool IsExpired(DateTime now, UserSessionPolicy policy) =>
        now >= policy.Expiry(AuthenticatedAt, KeepSignedIn, LastSeenTime);

    /// <summary>仍有效的会话，可翻译成 SQL。</summary>
    public static Expression<Func<UserSession, bool>> ActiveAt(DateTime now, UserSessionPolicy policy)
    {
        var standardCutoff = now - policy.SessionLifetime;
        var keepSignedInCutoff = now - policy.KeepSignedInLifetime;
        var idleCutoff = now - policy.IdleTimeout;
        var allowKeepSignedIn = policy.AllowKeepSignedIn;
        return session => allowKeepSignedIn && session.KeepSignedIn
            ? session.AuthenticatedAt > keepSignedInCutoff && session.LastSeenTime > idleCutoff
            : session.AuthenticatedAt > standardCutoff;
    }

    /// <summary>已结束的会话，与有效会话条件互补。</summary>
    public static Expression<Func<UserSession, bool>> ExpiredAt(DateTime now, UserSessionPolicy policy)
    {
        var active = ActiveAt(now, policy);
        return Expression.Lambda<Func<UserSession, bool>>(Expression.Not(active.Body), active.Parameters);
    }

    /// <summary>记一次活跃。距上次记录不足 <see cref="TouchInterval"/> 时不改动，返回 false，调用方据此省掉写库。</summary>
    public bool Touch(DateTime now, string? ipAddress)
    {
        if (now - LastSeenTime < TouchInterval)
            return false;

        LastSeenTime = now;
        if (!string.IsNullOrEmpty(ipAddress))
            IpAddress = Truncate(ipAddress, IpAddressMaxLength);
        return true;
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
#endif
