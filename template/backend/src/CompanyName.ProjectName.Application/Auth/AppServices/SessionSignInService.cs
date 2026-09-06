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
    /// <param name="user">登录用户。</param>
    /// <param name="roleNames">
    /// 调用方已知的角色名。为 <see langword="null"/> 时回查数据库。
    /// 首次外部登录必须传入：此时角色关联行与用户同在一个未提交的边界内，回查得到空集合，
    /// 签发出的主体会少掉全部角色。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ClaimsPrincipal> SignInAsync(
        User user,
        List<string>? roleNames = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        EnsureAllowed(user, now);

        user.RecordLoginSuccess(now);
        await userRepository.UpdateAsync(user, cancellationToken);

        var identity = new ClaimsIdentity(AuthenticationSchemeNames.SessionCookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
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
