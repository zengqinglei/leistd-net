#if (LocalIdentity)
using System.Collections.Immutable;
using System.Security.Claims;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Claims;
using Leistd.Timing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class AuthPrincipalFactory(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    IClock clock,
    IOptions<OAuthOptions> oauthOptions) : IAuthPrincipalFactory
{
    /// <inheritdoc />
    public async Task<ClaimsPrincipal?> CreateAsync(
        Guid userId,
        IEnumerable<string>? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        // 签发点自己闭合检查：令牌一旦发出就在有效期内可用，此刻停用的账号不能再拿到新令牌。
        // 这与运行期的 ActiveUserRequirement 是两道独立关卡——签发与访问是两个时刻。
        if (user == null || user.GetAccessStatus(clock.Now) != UserAccessStatus.Allowed)
        {
            return null;
        }

        var roleNames = await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken);
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Name, user.DisplayName ?? user.Username);
        identity.SetClaim(Claims.PreferredUsername, user.Username);
        identity.SetClaim(Claims.Email, user.Email);

        if (IsHttpUrl(user.Avatar))
        {
            identity.SetClaim(Claims.Picture, user.Avatar!);
        }

        identity.SetClaims(Claims.Role, roleNames.ToImmutableArray());
        identity.SetClaim(CustomClaimTypes.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false");
        // 租户 claim：多租户解析链以它定案已登录用户的租户
        if (user.TenantId is { } tenantId)
        {
            identity.SetClaim(CustomClaimTypes.TenantId, tenantId.ToString());
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes?.Where(scope => !string.IsNullOrWhiteSpace(scope)) ??
                            [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles]);
        principal.SetResources(oauthOptions.Value.Resource);
        principal.SetDestinations(GetDestinations);

        return principal;
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object>?> CreateUserInfoAsync(
        Guid userId,
        ClaimsPrincipal tokenPrincipal,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        // 这里不重复检查访问状态，不是因为"令牌有效期内不再回查"——恰恰相反：默认授权策略的
        // ActiveUserRequirement 在**每个** HTTP 请求（含本端点）与每次 Hub 握手时都重新确认账号
        // 状态，禁用与锁定因此对已签发的 Cookie 与用户 Bearer 立即生效。持续撤权的边界在那条
        // 授权管道里；本方法只负责按已确认的主体投影 claim，再查一遍是同一件事做两遍。
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString()
        };

        if (tokenPrincipal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName ?? user.Username;
            claims[Claims.PreferredUsername] = user.Username;
            if (IsHttpUrl(user.Avatar))
            {
                claims[Claims.Picture] = user.Avatar!;
            }
        }

        if (tokenPrincipal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (tokenPrincipal.HasScope(Scopes.Roles))
        {
            // 角色取令牌里的那份而不是回查：userinfo 要反映这枚令牌代表的权限，
            // 回查会让它和令牌内容不一致。
            claims[Claims.Role] = tokenPrincipal.GetClaims(Claims.Role)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return claims;
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        return claim.Type switch
        {
            Claims.Subject =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],
            Claims.Name or Claims.PreferredUsername or Claims.Picture
                when claim.Subject?.HasScope(Scopes.Profile) == true =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],
            Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],
            Claims.Role when claim.Subject?.HasScope(Scopes.Roles) == true =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],
            CustomClaimTypes.IsSuperAdmin =>
            [
                Destinations.AccessToken
            ],
            CustomClaimTypes.TenantId =>
            [
                Destinations.AccessToken
            ],
            _ => []
        };
    }
}
#endif
