#if (LocalIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.Stores;
using Leistd.Security.Claims;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using CompanyName.ProjectName.Application.Auth.Abstractions;

namespace CompanyName.ProjectName.Application.Auth.Sessions;

/// <inheritdoc cref="IUserSessionValidator" />
/// <remarks>
/// 校验发生在认证阶段、多租户中间件之前，由本类按主体的租户声明自行切换上下文。
/// 有效的结果缓存 <see cref="UserSession.TouchInterval"/>；会话被撤销时由
/// <see cref="EventHandlers.UserSessionRevokedEventHandler"/> 作废对应缓存。
/// </remarks>
internal sealed class UserSessionValidator(
    IRepository<UserSession, Guid> sessionRepository,
    IRepository<User, Guid> userRepository,
    ICurrentTenant currentTenant,
    ITenantStore tenantStore,
    IUnitOfWorkManager unitOfWorkManager,
    IDistributedCache distributedCache,
    IRequestClientInfo clientInfo,
    IOptions<UserSessionOptions> options,
    IClock clock) : IUserSessionValidator
{
    private const string CacheKeyPrefix = "auth:session:";

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        // 没有会话声明的主体一律无效：不留"旧 Cookie 免检"的口子，否则撤销对它们不起作用
        if (ReadGuid(principal.FindFirst(CustomClaimTypes.SessionId)?.Value) is not { } sessionId ||
            ReadGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value) is not { } userId)
            return false;

        var cacheKey = CacheKey(sessionId);
        if (await distributedCache.GetStringAsync(cacheKey, cancellationToken) is not null)
            return true;

        var tenantId = ReadGuid(principal.FindFirst(CustomClaimTypes.TenantId)?.Value);
        string? tenantName = null;
        if (tenantId is { } id)
        {
            var tenant = await tenantStore.FindAsync(id, cancellationToken);
            // 租户已不存在或已停用：不在这里判会话无效，交给租户会话自恢复中间件（UseTenantSessionRecovery）。
            // 它注销的同时带上 X-Tenant-Invalid，前端据此清掉本地的租户选择；在这里拒绝只会得到一个普通 401，
            // 下次登录仍落到那个不可用的租户上。
            if (tenant is null || !tenant.IsActive)
                return true;
            tenantName = tenant.Name;
        }

        // 认证发生在多租户中间件之前，环境租户还没设：按主体的租户进到它的库里查
        using (currentTenant.Change(tenantId, tenantName))
        using (var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
        {
            var now = clock.Now;
            var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
            if (session is null || session.UserId != userId || session.IsExpired(now, options.Value.IdleTimeout))
                return false;

            // 经应用停用、删除账号时会话已被撤销；这里兜住绕过应用直接改库的情形，最迟在缓存到期后生效
            var user = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null || !user.AllowsExistingSessions(now))
                return false;

            if (session.Touch(now, clientInfo.IpAddress))
            {
                await sessionRepository.UpdateAsync(session, cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }

        // 缓存时长即最近活跃的更新间隔：缓存命中期间不查库，也就不写库
        await distributedCache.SetStringAsync(
            cacheKey,
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = UserSession.TouchInterval },
            cancellationToken);
        return true;
    }

    // 校验结果的缓存键；会话撤销时按同一个键作废
    internal static string CacheKey(Guid sessionId) => CacheKeyPrefix + sessionId.ToString("N");

    private static Guid? ReadGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
#endif
