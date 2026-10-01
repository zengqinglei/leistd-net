#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
using CompanyName.ProjectName.Application.Auth.SignIn;
using Leistd.ExceptionHandling;
using System.Security.Claims;
using Leistd.Timing;
using System.Text.Json.Nodes;
using CompanyName.ProjectName.Application.Auth.Constants;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.OAuth;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// OpenID Connect 协议端点：授权、令牌、注销与 userinfo。
/// </summary>
/// <remarks>
/// 本类是全项目唯一不继承 <c>BaseController</c> 的控制器，属规范允许的例外：这些端点要返回
/// <c>SignIn</c> / <c>SignOut</c> / <c>Challenge</c> / <c>Redirect</c> 这类结果并与 Cookie 方案交互，
/// 需要 <see cref="Controller"/> 而不是统一信封的基类；响应形状由 OpenIddict 与 OIDC 规范决定，
/// 不能被包成项目的统一信封。
///
/// 端点只做协议映射：主体装配与用户解析在 <see cref="IAuthPrincipalFactory"/>，
/// 这里只把"装配不出来"翻译成对应的协议响应。<b>签发点</b>的账号状态检查在那个工厂里；
/// 运行期的撤权靠停用、删除账号时撤销令牌，由令牌记录校验在认证阶段拒绝，与本控制器无关。
/// </remarks>
public sealed class ConnectController(
    IAuthPrincipalFactory principalFactory,
    IUserSessionAppService sessionAppService,
    IOptions<OAuthOptions> oauthOptions,
    IOptions<ClaimTypeOptions> claimTypes,
    IOpenIddictApplicationManager applications,
    IClock clock) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect authorization request is unavailable. "
                + "This means the OpenIddict server middleware is not wired for this endpoint.");

        var result = await HttpContext.AuthenticateAsync(AuthenticationSchemeNames.SessionCookie);
        var authenticationTime = result.Principal?.GetClaim(Claims.AuthenticationTime);
        var fresh = long.TryParse(authenticationTime, out var authenticatedAt);
        var reauthenticate = request.HasPromptValue(PromptValues.Login) ||
            request.MaxAge is { } maxAge && (!fresh || new DateTimeOffset(DateTime.SpecifyKind(clock.Now, DateTimeKind.Utc)).ToUnixTimeSeconds() - authenticatedAt >= maxAge);
        if (!result.Succeeded || result.Principal == null || reauthenticate)
        {
            if (request.HasPromptValue(PromptValues.None))
                return ProtocolError(Errors.LoginRequired);
            // 去掉已兑现的重新认证参数，登录完成后的回跳不会再次要求登录。
            var parameters = Request.HasFormContentType ? Request.Form.AsEnumerable() : Request.Query.AsEnumerable();
            var returnUrl = Request.PathBase + Request.Path + QueryString.Create(parameters
                .Where(parameter => parameter.Key is not ("prompt" or "max_age"))
                .Select(parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value)));
            var prompts = request.GetPromptValues().Where(prompt => prompt != PromptValues.Login).ToArray();
            if (prompts.Length > 0) returnUrl += QueryString.Create("prompt", string.Join(' ', prompts)).Value?.Replace('?', '&');

            return Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}" +
                (reauthenticate ? "&reauthenticate=true" : string.Empty));
        }

        var subject = claimTypes.Value.FindUserId(result.Principal);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = await principalFactory.CreateAsync(userId, request.GetScopes(), cancellationToken);
        if (principal == null)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (fresh)
            principal.SetClaim(Claims.AuthenticationTime, authenticatedAt)
                .SetDestinations(claim => claim.Type == Claims.AuthenticationTime
                    ? [Destinations.IdentityToken] : claim.GetDestinations());
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await sessionAppService.EndCurrentSessionAsync(cancellationToken);
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> ExchangeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect token request is unavailable. "
                + "This means the OpenIddict server middleware is not wired for this endpoint.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal is null)
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var scopes = request.GetScopes().Any()
                ? request.GetScopes()
                : result.Principal.GetScopes();
            // 用户与租户都取自授权码/刷新令牌的主体：本请求没有用户身份，解析链只能得出宿主
            var principal = await principalFactory.CreateFromTokenAsync(result.Principal, scopes, cancellationToken);
            if (principal == null)
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsTokenExchangeGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal;
            var app = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken);
            // 官方 ValidateAuthorizedParty 之外的严格单跳策略：来源受众必须是调用方 API。
            if (subject is null || app is null ||
                !await applications.HasClientTypeAsync(app, ClientTypes.Confidential, cancellationToken) ||
                !subject.HasAudience(request.ClientId!) || subject.HasClaim(claim => claim.Type == "act") ||
                request.GetAudiences().Length != 1 ||
                request.GetScopes().Length != 1 || request.GetScopes()[0] != request.GetAudiences()[0] ||
                !OAuthScopes.All(oauthOptions.Value).Any(scope => !scope.MachineOnly &&
                    scope.Name == request.GetAudiences()[0] && scope.Resources.Count == 1) ||
                subject.GetExpirationDate() is not { } expiry)
                return ProtocolError(Errors.InvalidGrant);
            var principal = await principalFactory.CreateFromTokenAsync(subject, request.GetScopes(), cancellationToken);
            if (principal is null) return ProtocolError(Errors.InvalidGrant);
            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (var claim in identity.Claims.Where(claim => claim.Type is Claims.Role or CustomClaimTypes.IsSuperAdmin or Claims.AuthenticationTime).ToArray())
                identity.RemoveClaim(claim);
            identity.SetClaim("act", new JsonObject { [Claims.Subject] = ClientSubject.Format(request.ClientId!) });
            // 用户资料重新取自身份库，目标 scope 不含 profile/email 时仍保留投影所需的资料。
            principal.SetResources(request.GetAudiences());
            principal.SetDestinations(_ => [Destinations.AccessToken]);
            principal.SetAccessTokenLifetime(TimeSpan.FromSeconds(120));
            principal.SetExpirationDate(expiry);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // client_credentials 流程：token 代表客户端应用自身，无用户上下文
            var identity = new ClaimsIdentity(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                Claims.Name,
                Claims.Role);

            // 机器主体的 sub 契约由框架 ClientSubject 定义（client:<client_id>），签发端与
            // 消费端（机器端点授权）共用同一处定义，理由见该类型的注释。
            SubjectClaims.Set(identity, claimTypes.Value, ClientSubject.Format(request.ClientId!));
            identity.AddClaim(new Claim(Claims.Name, request.ClientId!));

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(OAuthScopes.ResourcesOf(oauthOptions.Value, principal.GetScopes()));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new BusinessException(AuthErrorCodes.UnsupportedGrantType, $"Unsupported grant type: {request.GrantType}")
            .WithData("GrantType", request.GrantType);
    }

    private ForbidResult ProtocolError(string error) => Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error
    }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Produces("application/json")]
    public async Task<IActionResult> UserInfoAsync(CancellationToken cancellationToken)
    {
        var claims = await principalFactory.CreateUserInfoAsync(User, cancellationToken);
        if (claims == null)
        {
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Ok(claims);
    }
}
#endif
