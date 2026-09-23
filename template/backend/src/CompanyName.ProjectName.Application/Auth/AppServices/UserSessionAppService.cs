#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Application.Auth.Mappings;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ExceptionHandling;
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.Security.Users;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <inheritdoc cref="IUserSessionAppService" />
internal sealed class UserSessionAppService(
    IRepository<UserSession, Guid> sessionRepository,
    UserSessionDomainService userSessionDomainService,
    ICurrentUser currentUser,
    IUnitOfWorkManager unitOfWorkManager,
    IOperationRecorder operationRecorder,
    IOptions<UserSessionOptions> sessionOptions,
    IClock clock,
    IObjectMapper objectMapper) : BaseAppService, IUserSessionAppService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSessionOutputDto>> GetCurrentUserSessionsAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id!.Value;
        var currentSessionId = currentUser.GetSessionId();
        var cutoff = clock.Now - sessionOptions.Value.IdleTimeout;
        var sessions = await sessionRepository.GetListAsync(
            s => s.UserId == userId && s.LastSeenTime > cutoff,
            cancellationToken);

        var context = new Dictionary<string, object>();
        if (currentSessionId is { } current)
            context[AuthProfile.CurrentSessionIdKey] = current;

        return sessions
            .Select(s => objectMapper.Map<UserSession, UserSessionOutputDto>(s, context))
            // 当前设备置顶：用户找"这是哪台"的第一眼总是自己手上这台；其余按最近活跃
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastSeenTime)
            .ToList();
    }

    /// <inheritdoc />
    public async Task RevokeCurrentUserSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == currentUser.GetSessionId())
        {
            throw new BusinessException(AuthErrorCodes.CannotRevokeCurrentSession, "Use sign-out to end the current session.")
                ;
        }

        var revoked = await userSessionDomainService.RevokeAsync(currentUser.Id!.Value, sessionId, cancellationToken);
        if (revoked is null)
            return;

        // 目标取那台设备的 IP：会话 Id 对看记录的人没有意义，IP 至少能和登录记录对上
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthSessionRevoked,
            OperationTarget.For(revoked.Id.ToString(), revoked.IpAddress ?? "-"),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RevokeOtherCurrentUserSessionsAsync(CancellationToken cancellationToken = default)
    {
        var count = await userSessionDomainService.RevokeAllAsync(
            currentUser.Id!.Value,
            currentUser.GetSessionId(),
            cancellationToken);

        if (count > 0)
        {
            await operationRecorder.RecordSucceededAsync(
                OperationRecordActions.AuthOtherSessionsRevoked,
                OperationTarget.For(currentUser.Id!.Value, currentUser.Name ?? currentUser.Username),
                OperationRecordAuthorizations.AuthenticatedSelf,
                cancellationToken);
        }

        return count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 新开工作单元：调用处可能刚在别的租户上下文里写过库，请求作用域里的上下文未必绑着当前租户的库。
    /// 租户上下文沿用环境值——多租户中间件已按会话主体的租户声明设好。
    /// </remarks>
    public async Task EndCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId || currentUser.GetSessionId() is not { } sessionId)
            return;

        using var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
        await userSessionDomainService.RevokeAsync(userId, sessionId, cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);
    }
}
#endif
