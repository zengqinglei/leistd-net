#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Timing;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Domain.Auth.DomainServices;

/// <summary>
/// 登录会话的登记与撤销规则。
/// </summary>
/// <remarks>
/// <para>撤销即删除，并由 <see cref="UserSession.Revoke(DateTime)"/> 发出事件：会话校验结果带短缓存，
/// 订阅方在事务提交后作废它，撤销才能对已发出的 Cookie 立即生效。</para>
/// <para>会话落在用户所在租户的库里，登记与撤销跟随调用方当时的租户上下文。</para>
/// </remarks>
public class UserSessionDomainService(
    IRepository<UserSession, Guid> sessionRepository,
    IOptions<UserSessionOptions> options,
    IClock clock)
{
    /// <summary>
    /// 为一次登录登记会话，返回的会话 Id 写进 <c>sid</c> 声明。
    /// </summary>
    /// <remarks>顺手删掉此人已过期的会话：表里只留有效会话，不必另起清理作业。</remarks>
    public async Task<UserSession> StartAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        string? impersonatorName,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        var cutoff = now - options.Value.IdleTimeout;
        await sessionRepository.DeleteManyAsync(s => s.UserId == userId && s.LastSeenTime <= cutoff, cancellationToken);

        return await sessionRepository.InsertAsync(
            new UserSession(userId, now, ipAddress, userAgent, impersonatorName),
            cancellationToken);
    }

    /// <summary>撤销某用户的一个会话；不存在（或不属于该用户）时返回 null。</summary>
    public async Task<UserSession?> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
            return null;

        session.Revoke(clock.Now);
        await sessionRepository.DeleteAsync(session, cancellationToken);
        return session;
    }

    /// <summary>撤销某用户的全部会话（可保留一个），返回撤销的个数。</summary>
    /// <remarks>改密码、管理员重置密码、两步验证变更时调用：凭据变了，此前建立的会话不应继续有效。</remarks>
    public async Task<int> RevokeAllAsync(Guid userId, Guid? exceptSessionId, CancellationToken cancellationToken = default)
    {
        var sessions = (await sessionRepository.GetListAsync(
            s => s.UserId == userId && s.Id != exceptSessionId,
            cancellationToken)).ToList();
        if (sessions.Count == 0)
            return 0;

        var now = clock.Now;
        foreach (var session in sessions)
        {
            session.Revoke(now);
        }

        await sessionRepository.DeleteManyAsync(sessions, cancellationToken);
        return sessions.Count;
    }
}
#endif
