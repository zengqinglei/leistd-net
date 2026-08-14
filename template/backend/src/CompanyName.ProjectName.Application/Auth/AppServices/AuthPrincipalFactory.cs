#if (IncludeIdentity)
using System.Collections.Immutable;
using System.Security.Claims;
using CompanyName.ProjectName.Domain.Auth.Options;
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Users.DomainServices;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class AuthPrincipalFactory(
#if (IncludeRoles)
    UserDomainService userDomainService,
#endif
    IOptions<OAuthOptions> oauthOptions) : IAuthPrincipalFactory
{
    public async Task<ClaimsPrincipal> CreateAsync(
        User user,
        IEnumerable<string>? scopes = null,
        CancellationToken cancellationToken = default)
    {
#if (IncludeRoles)
        var roleNames = await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken);
#endif
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Name, user.DisplayName ?? user.Username);
        identity.SetClaim(Claims.PreferredUsername, user.Username);
        identity.SetClaim(Claims.Email, user.Email);

        if (IsHttpUrl(user.Avatar))
        {
            identity.SetClaim(Claims.Picture, user.Avatar!);
        }

#if (IncludeRoles)
        identity.SetClaims(Claims.Role, roleNames.ToImmutableArray());
#endif
        identity.SetClaim(CustomClaimTypes.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false");
#if (TenancyEnabled)
        // 租户 claim：多租户解析链以它定案已登录用户的租户
        if (user.TenantId is { } tenantId)
        {
            identity.SetClaim(CustomClaimTypes.TenantId, tenantId.ToString());
        }
#endif

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes?.Where(scope => !string.IsNullOrWhiteSpace(scope)) ??
#if (IncludeRoles)
                            [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles]);
#else
                            [Scopes.OpenId, Scopes.Profile, Scopes.Email]);
#endif
        principal.SetResources(oauthOptions.Value.Resource);
        principal.SetDestinations(GetDestinations);

        return principal;
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
#if (IncludeRoles)
            Claims.Role when claim.Subject?.HasScope(Scopes.Roles) == true =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],
#endif
            CustomClaimTypes.IsSuperAdmin =>
            [
                Destinations.AccessToken
            ],
#if (TenancyEnabled)
            CustomClaimTypes.TenantId =>
            [
                Destinations.AccessToken
            ],
#endif
            _ => []
        };
    }
}
#endif
