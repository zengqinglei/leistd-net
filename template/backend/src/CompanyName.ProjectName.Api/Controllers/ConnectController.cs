#if (IncludeIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.Api.Controllers;

public class ConnectController(
    IRepository<User, Guid> userRepository,
    IAuthPrincipalFactory principalFactory,
    IOptions<OAuthOptions> oauthOptions) : Controller
{
    /// <summary>
    /// 机器主体 <c>sub</c> 的前缀，用来与自然人主体隔离命名空间。
    /// </summary>
    /// <remarks>
    /// 自然人主体的 <c>sub</c> 是用户 Id（GUID），解析方按 <c>Guid.TryParse</c> 认领；
    /// 只要机器主体也可能落进 GUID 形态，冒充就成立。前缀让两者在结构上不可碰撞。
    /// </remarks>
    public const string ClientSubjectPrefix = "client:";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect authorization request is unavailable.");

        var result = await HttpContext.AuthenticateAsync("MyProjectCookie");
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

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.IsActive || user.IsLockedOut())
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var principal = await principalFactory.CreateAsync(user, request.GetScopes(), cancellationToken);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync("MyProjectCookie");
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> ExchangeAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect token request is unavailable.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal?.GetClaim(Claims.Subject);
            if (!result.Succeeded || !Guid.TryParse(subject, out var userId))
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var user = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || !user.IsActive || user.IsLockedOut())
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var scopes = request.GetScopes().Any()
                ? request.GetScopes()
                : result.Principal?.GetScopes() ?? [];
            var principal = await principalFactory.CreateAsync(user, scopes, cancellationToken);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // client_credentials 流程：token 代表客户端应用自身，无用户上下文
            var identity = new ClaimsIdentity(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                Claims.Name,
                Claims.Role);

            // subject 带 client: 前缀，与自然人主体分处两个不可碰撞的命名空间。
            // 自然人的 sub 是用户 Id（GUID），主体解析按 Guid.TryParse 认领；直接写裸 client_id
            // 就等于把 sub 的命名空间共享给了机器主体——而 client_id 由创建者任意指定，
            // 挑一个已存在的用户 Id（用户管理、审计日志、业务数据里都拿得到）即可让机器令牌
            // 被解析成那个人，继承他的直授、角色乃至超管身份。加前缀后 Guid.TryParse 必然失败，
            // 这条冒充路径在结构上就不存在，不依赖任何对 client_id 取值的输入校验。
            identity.AddClaim(new Claim(Claims.Subject, $"{ClientSubjectPrefix}{request.ClientId!}"));
            identity.AddClaim(new Claim(Claims.Name, request.ClientId!));

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            var resources = new[] { oauthOptions.Value.Resource };
            principal.SetResources(resources);

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new BadRequestException($"Unsupported grant type: {request.GrantType}")
#if (IncludeLocalization)
            .WithLocalization("Auth:UnsupportedGrantType")
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

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString()
        };

        if (User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName ?? user.Username;
            claims[Claims.PreferredUsername] = user.Username;
            if (Uri.TryCreate(user.Avatar, UriKind.Absolute, out var avatarUri) &&
                (avatarUri.Scheme == Uri.UriSchemeHttp || avatarUri.Scheme == Uri.UriSchemeHttps))
            {
                claims[Claims.Picture] = user.Avatar;
            }
        }

        if (User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

#if (IncludeRoles)
        if (User.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = User.GetClaims(Claims.Role)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
#endif

        return Ok(claims);
    }
}
#endif
