#if (LocalIdentity)
using Leistd.ExceptionHandling;
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth;
using CompanyName.ProjectName.Application.Auth.AppServices;
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
/// 运行期的持续撤权在授权管道的 <c>ActiveUserRequirement</c>，与本控制器无关。
/// </remarks>
public sealed class ConnectController(
    IAuthPrincipalFactory principalFactory,
    IOptions<OAuthOptions> oauthOptions) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InternalServerException(
                "The OpenID Connect authorization request is unavailable. "
                + "This means the OpenIddict server middleware is not wired for this endpoint.");

        var result = await HttpContext.AuthenticateAsync(AuthenticationSchemeNames.SessionCookie);
        if (!result.Succeeded || result.Principal == null)
        {
            var returnUrl = Request.PathBase + Request.Path + QueryString.Create(
                Request.HasFormContentType
                    ? Request.Form.Select(parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value))
                    : Request.Query.Select(parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value)));

            return Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var subject = result.Principal.GetClaim(Claims.Subject) ??
                      result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = await principalFactory.CreateAsync(userId, request.GetScopes(), cancellationToken);
        if (principal == null)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> ExchangeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InternalServerException(
                "The OpenID Connect token request is unavailable. "
                + "This means the OpenIddict server middleware is not wired for this endpoint.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal?.GetClaim(Claims.Subject);
            if (!result.Succeeded || !Guid.TryParse(subject, out var userId))
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var scopes = request.GetScopes().Any()
                ? request.GetScopes()
                : result.Principal?.GetScopes() ?? [];
            var principal = await principalFactory.CreateAsync(userId, scopes, cancellationToken);
            if (principal == null)
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

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
            // 消费端（服务间调用的用户上下文恢复）共用同一处定义，理由见该类型的注释。
            identity.AddClaim(new Claim(Claims.Subject, ClientSubject.Format(request.ClientId!)));
            identity.AddClaim(new Claim(Claims.Name, request.ClientId!));

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            var resources = new[] { oauthOptions.Value.Resource };
            principal.SetResources(resources);

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new BadRequestException($"Unsupported grant type: {request.GrantType}")
#if (IncludeLocalization)
            .WithCode("Auth:UnsupportedGrantType")
            .WithData("GrantType", request.GrantType)
#endif
            ;
    }

    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Produces("application/json")]
    public async Task<IActionResult> UserInfoAsync(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(Claims.Subject)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = await principalFactory.CreateUserInfoAsync(userId, User, cancellationToken);
        if (claims == null)
        {
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Ok(claims);
    }
}
#endif
