#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Auth.SignIn;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Timing;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ExceptionHandling;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;

namespace CompanyName.ProjectName.Application.Auth.Policies;

/// <summary>
/// 再认证的失败约束：已登录的人要再证明一次自己知道口令或验证码时，失败按登录的同一套计数与锁定处理。
/// </summary>
/// <remarks>
/// <para><b>为什么需要这一层。</b>登录页有锁定，再认证入口（改口令、停用两步验证、重发恢复码）
/// 如果没有，那条"连续失败 N 次锁定"的设置就名不副实：会话被盗用的人能在改口令接口上
/// <b>无限次</b>猜当前口令，把登录页的锁定整个绕过去。这类再认证失败按 OWASP ASVS 与
/// NIST 800-63B 都应当既记日志、又与登录共用失败计数。</para>
/// <para><b>为什么不新造一个计数器。</b>共用 <see cref="User.RecordAccessFailed"/> 与
/// <see cref="User.GetAccessStatus"/>，管理员调 <c>Security.LockoutMaxFailedAttempts</c> 时
/// 才不会出现"有的地方算、有的地方不算"。</para>
/// <para><b><see cref="EnsureAllowedAsync"/> 不能省。</b>临时锁定按设计只挡新登录、不踢已有会话
/// （见 <see cref="User.AllowsExistingSessions"/>）。只计数不拦的话，攻击者猜到锁定为止、
/// 自己手里那个被盗会话照常用、接着猜，结果只是把本人挡在登录页外——比不改更糟。</para>
/// </remarks>
internal interface IReauthenticationGuard
{
    /// <summary>锁定期内直接拒绝，不进入校验。必须在比对口令或验证码<b>之前</b>调用。</summary>
    Task EnsureAllowedAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记一次再认证失败（计数、必要时锁定、留下审计），并<b>返回该抛给调用方的异常</b>。
    /// </summary>
    /// <remarks>
    /// 返回异常而不是由调用方自己构造：<b>这一次恰好触发锁定时，答案不再是"口令不正确"而是"已锁定"</b>
    /// ——与登录页一致（见 <c>LoginLockoutTests</c>）。把这个判断留给三个调用点各写一次，
    /// 漏掉的那个会在锁定后继续回"口令不正确"，让人以为还能再试。
    /// 用法固定是 <c>throw await guard.RejectAsync(...)</c>。
    /// </remarks>
    /// <param name="user">当前用户。</param>
    /// <param name="action">业务动作码，与该操作成功路径使用的值逐字一致。</param>
    /// <param name="failureCode">失败原因码。</param>
    /// <param name="failureMessage">未触发锁定时向调用方公开的安全文案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<BusinessException> RejectAsync(
        User user,
        string action,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IReauthenticationGuard" />
internal sealed class ReauthenticationGuard(
    IRepository<User, Guid> userRepository,
    ILoginSecurityPolicyProvider loginSecurityPolicy,
    IOperationRecorder operationRecorder,
    ISecurityAlertPublisher securityAlerts,
    IClock clock) : IReauthenticationGuard
{
    /// <inheritdoc />
    public async Task EnsureAllowedAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = clock.Now;
        if (user.GetAccessStatus(now) == UserAccessStatus.LockedOut)
        {
            throw SessionSignInService.LockedOut(user, now);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<BusinessException> RejectAsync(
        User user,
        string action,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var policy = await loginSecurityPolicy.GetAsync(cancellationToken);
        var now = clock.Now;
        var lockedOut = user.RecordAccessFailed(now, policy.Lockout);

        // 调用方不在 [UnitOfWork] 内，所以这次写入即时落库，不随紧接着的抛出回滚——
        // 计数被回滚等于没有计数。给再认证入口加工作单元前先处理这里。
        await userRepository.UpdateAsync(user, cancellationToken);

        var target = OperationTarget.For(user.Id, user.DisplayName ?? user.Username);
        await operationRecorder.RecordFailedAsync(
            action,
            target,
            // 与登录侧不同：此刻主体已建立，是本人在对自己做这件事
            OperationRecordAuthorizations.AuthenticatedSelf,
            OperationFailure.FromCode(failureCode));

        if (!lockedOut)
        {
            return new BusinessException(failureCode, failureMessage);
        }

        // 只在"这一次恰好触发锁定"时再记一条：一个锁定期内至多一条，写入量有界。
        // 与 AuthAppService 登录侧的同名处理成对，差别只在授权依据——改一边记得看另一边。
        await securityAlerts.PublishAsync(
            user.Id,
            new SecurityAlert(SecurityAlertKind.LockedOut, Until: user.LockoutEnd),
            cancellationToken);
        await operationRecorder.RecordFailedAsync(
            OperationRecordActions.AuthLockedOut,
            target,
            OperationRecordAuthorizations.AuthenticatedSelf,
            OperationFailure.FromCode(AuthErrorCodes.UserTemporarilyLockedOut, new Dictionary<string, object?>
            {
                ["maxFailedAttempts"] = policy.Lockout.MaxFailedAttempts,
                ["minutes"] = (int)policy.Lockout.Duration.TotalMinutes
            }));

        return SessionSignInService.LockedOut(user, now);
    }
}
#endif
