#if (LocalIdentity)
using System.Security.Claims;

namespace CompanyName.ProjectName.Application.Auth.Sessions;

/// <summary>
/// 会话 Cookie 的服务端校验：Cookie 里的会话仍然有效才放行。
/// </summary>
public interface IUserSessionValidator
{
    /// <summary>
    /// 校验会话主体。会话已撤销、已过期或不属于该用户时返回 false。
    /// 账号本身是否可用不在此判定，由默认授权策略负责。
    /// </summary>
    /// <remarks>
    /// 结果按 <c>UserSession.TouchInterval</c> 缓存：本实例上的撤销立即生效，
    /// 其他实例在未共享缓存（未配 Redis）时最多滞后一个间隔。
    /// </remarks>
    Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
#endif
