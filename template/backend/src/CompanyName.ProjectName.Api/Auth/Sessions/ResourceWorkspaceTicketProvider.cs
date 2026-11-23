#if (TenantWorkspace && ResourceBrowserSession)
using System.Globalization;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Shared;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Stores;
using Leistd.ServiceClient.Exceptions;
using Leistd.Security.AspNetCore.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Client.OpenIddictClientModels;

namespace CompanyName.ProjectName.Api.Auth.Sessions;

/// <summary>工作区令牌按本人的服务端票据、主体与租户缓存，不更换 Cookie。</summary>
internal sealed class ResourceWorkspaceTicketProvider(IMemoryCache cache, IServiceProvider services,
    IOptions<RemoteIdentityOptions> remoteIdentity, TimeProvider clock)
{
    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    public async Task<AuthenticateResult?> GetAsync(HttpContext context, AuthenticateResult home,
        TenantConfiguration tenant, CancellationToken cancellationToken)
    {
        var subjectToken = home.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(subjectToken)) return null;
        var subject = home.Principal?.FindFirst("sub")?.Value;
        home.Properties!.Items.TryGetValue(DistributedTicketStore.TicketKeyProperty, out var ticketKey);
        var key = (AuthenticationSchemeNames.Workspace, ticketKey, subject, tenant.Id);
        var canCache = !string.IsNullOrEmpty(ticketKey) && !string.IsNullOrEmpty(subject);
        if (!canCache || !cache.TryGetValue(key, out CachedToken? token) || token!.ExpiresAt <= clock.GetUtcNow())
        {
            var target = remoteIdentity.Value;
            TokenExchangeAuthenticationResult result;
            try
            {
                result = await services.GetRequiredService<OpenIddictClientService>().AuthenticateWithTokenExchangeAsync(new TokenExchangeAuthenticationRequest
                {
                    Issuer = target.IssuerUri, SubjectToken = subjectToken, SubjectTokenType = TokenTypeIdentifiers.AccessToken,
                    RequestedTokenType = TokenTypeIdentifiers.AccessToken, Audiences = [target.Audience!],
                    Scopes = [target.Scope ?? target.Audience!], DisableUserInfo = true, CancellationToken = cancellationToken,
                    AdditionalTokenRequestParameters = new() { ["workspace"] = tenant.Name }
                });
            }
            // 框架在官方客户端上统一接手令牌响应，OAuth 拒绝以 RemoteServiceException 抛出（保留错误码）
            catch (RemoteServiceException exception) when (exception is { IsOAuthError: true, ErrorCode: Errors.InvalidTarget })
            {
                return null;
            }
            catch (RemoteServiceException exception) when (exception is { IsOAuthError: true, ErrorCode: Errors.InvalidGrant })
            {
                // 身份服务不再接受本人凭据（会话结束、令牌撤销、账号停用）：与框架的 Token Exchange 同一口径，答 401 让前端重新登录
                throw new AuthenticationRequiredException(exception);
            }
            if (result.IssuedTokenExpirationDate is not { } expiry || expiry <= clock.GetUtcNow()) return null;
            token = new(result.IssuedToken, expiry);
            if (canCache)
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
