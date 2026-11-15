#if (TenantWorkspace && ResourceBrowserSession)
using System.Globalization;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Shared;
using Leistd.MultiTenancy.Stores;
using Leistd.Security.AspNetCore.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Client.OpenIddictClientModels;

namespace CompanyName.ProjectName.Api.Auth.Sessions;

/// <summary>工作区令牌按本人的服务端票据与租户缓存，不更换 Cookie。</summary>
internal sealed class ResourceWorkspaceTicketProvider(IMemoryCache cache, IServiceProvider services,
    IOptions<RemoteIdentityOptions> remoteIdentity, TimeProvider clock)
{
    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    public async Task<AuthenticateResult?> GetAsync(HttpContext context, AuthenticateResult home,
        TenantConfiguration tenant, CancellationToken cancellationToken)
    {
        var subject = home.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(subject)) return null;
        home.Properties!.Items.TryGetValue(DistributedTicketStore.TicketKeyProperty, out var ticketKey);
        var key = (AuthenticationSchemeNames.Workspace, ticketKey, tenant.Id);
        if (ticketKey is null || !cache.TryGetValue(key, out CachedToken? token) || token!.ExpiresAt <= clock.GetUtcNow())
        {
            var target = remoteIdentity.Value;
            TokenExchangeAuthenticationResult result;
            try
            {
                result = await services.GetRequiredService<OpenIddictClientService>().AuthenticateWithTokenExchangeAsync(new TokenExchangeAuthenticationRequest
                {
                    Issuer = target.IssuerUri, SubjectToken = subject, SubjectTokenType = TokenTypeIdentifiers.AccessToken,
                    RequestedTokenType = TokenTypeIdentifiers.AccessToken, Audiences = [target.Audience!],
                    Scopes = [target.Scope ?? target.Audience!], DisableUserInfo = true, CancellationToken = cancellationToken,
                    AdditionalTokenRequestParameters = new() { ["workspace"] = tenant.Name }
                });
            }
            catch (OpenIddict.Abstractions.OpenIddictExceptions.ProtocolException exception) when (exception.Error == Errors.InvalidTarget)
            {
                return null;
            }
            if (result.IssuedTokenExpirationDate is not { } expiry || expiry <= clock.GetUtcNow()) return null;
            token = new(result.IssuedToken, expiry);
            if (ticketKey is not null)
                cache.Set(key, token, new MemoryCacheEntryOptions { AbsoluteExpiration =
                    expiry < clock.GetUtcNow().AddMinutes(4) ? expiry : clock.GetUtcNow().AddMinutes(4) });
        }
        var principal = await ResourceSessionRefresher.ValidateAccessTokenAsync(context, token.Value, cancellationToken);
        var properties = new AuthenticationProperties { ExpiresUtc = token.ExpiresAt };
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = token.Value },
            new AuthenticationToken { Name = "expires_at", Value = token.ExpiresAt.ToString("o", CultureInfo.InvariantCulture) }
        ]);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, properties, AuthenticationSchemeNames.Workspace));
    }
}
#endif
