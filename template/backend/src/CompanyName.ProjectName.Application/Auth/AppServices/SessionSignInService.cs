using System.Security.Claims;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Claims;
using Leistd.Timing;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

internal sealed class SessionSignInService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    IClock clock)
{
    // OIDC 标准声明名，ICurrentUser 按它们分别读用户名与显示名。
    private const string PreferredUsernameClaimType = "preferred_username";
    private const string DisplayNameClaimType = "name";

    /// <param name="user">登录用户。</param>
    /// <param name="roleNames">
    /// 调用方已知的角色名。为 <see langword="null"/> 时回查数据库。
    /// 首次外部登录必须传入：此时角色关联行与用户同在一个未提交的边界内，回查得到空集合，
    /// 签发出的主体会少掉全部角色。
    /// </param>
    /// <param name="additionalClaims">
    /// 追加到主体上的额外声明（模拟登录的发起人声明）。会话主体是这些声明的唯一载体——
    /// 已认证请求的租户由 cookie claim 定案，请求头改写不了，所以模拟态也必须落在这里。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 模拟登录复用本方法，因此 <c>RecordLoginSuccess</c> 也会记在被模拟的账号上：
    /// 换来的是访问校验（禁用、锁定）只有一条实现，不会出现"正常登录拒绝、模拟登录放行"的偏差。
    /// 代价是该账号的"最近登录"含模拟进入的时刻，排查登录异常时需要结合审计日志区分。
    /// </remarks>
    public async Task<ClaimsPrincipal> SignInAsync(
        User user,
        List<string>? roleNames = null,
        IEnumerable<Claim>? additionalClaims = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        EnsureAllowed(user, now);

        user.RecordLoginSuccess(now);
        await userRepository.UpdateAsync(user, cancellationToken);

        var identity = new ClaimsIdentity(AuthenticationSchemeNames.SessionCookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        // ICurrentUser 的约定：Username 取 preferred_username，Name 是显示名、取 name。
        // 只写 ClaimTypes.Name 时两者都读到登录名，操作记录的操作人列就与目标列口径不一——
        // 同一个人在这边是 "admin"，在那边（目标名按 DisplayName ?? Username）是 "System Administrator"。
        // ClaimTypes.Name 保持登录名不动：它是 Identity.Name，框架外的中间件按它认人。
        identity.AddClaim(new Claim(PreferredUsernameClaimType, user.Username));
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(DisplayNameClaimType, user.DisplayName));
        }
        identity.AddClaim(new Claim(CustomClaimTypes.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false"));

        if (user.TenantId is { } tenantId)
        {
            identity.AddClaim(new Claim(CustomClaimTypes.TenantId, tenantId.ToString()));
        }

        foreach (var roleName in roleNames
            ?? await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken))
        {
            identity.AddClaim(new Claim("role", roleName));
        }

        foreach (var claim in additionalClaims ?? [])
        {
            identity.AddClaim(claim);
        }

        return new ClaimsPrincipal(identity);
    }

    private static void EnsureAllowed(User user, DateTime now)
    {
        var accessStatus = user.GetAccessStatus(now);
        switch (accessStatus)
        {
            case UserAccessStatus.Allowed:
                return;
            case UserAccessStatus.Disabled:
                throw new UnauthorizedException($"Login failed: user is disabled - user: {user.Username}")
#if (IncludeLocalization)
                    .WithCode("Auth:UserDisabled")
                    .WithData("Username", user.Username)
#endif
                    ;
            case UserAccessStatus.LockedOut:
                throw new UnauthorizedException($"Login failed: user is locked out - user: {user.Username}, locked until: {user.LockoutEnd}")
#if (IncludeLocalization)
                    .WithCode("Auth:UserLockedOut")
                    .WithData("Username", user.Username)
                    .WithData("LockoutEnd", user.LockoutEnd)
#endif
                    ;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(accessStatus),
                    accessStatus,
                    "Unsupported user access status.");
        }
    }
}
