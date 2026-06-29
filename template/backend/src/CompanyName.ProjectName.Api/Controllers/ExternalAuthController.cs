#if (IncludeExternalLogin)
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 外部认证控制器
/// </summary>
[Route("api/v1/external-auth")]
public class ExternalAuthController(
    IExternalAuthAppService externalAuthAppService,
    UserDomainService userDomainService) : BaseController
{
    /// <summary>
    /// 获取外部登录 URL（GitHub, Google）
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{provider}/login-url")]
    public ExternalLoginUrlOutputDto GetLoginUrl(string provider)
    {
        return externalAuthAppService.GetLoginUrl(provider);
    }

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{provider}/callback")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CallbackAsync(
        string provider,
        [FromBody] ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken)
    {
        var user = await externalAuthAppService.AuthenticateExternalUserAsync(provider, request, cancellationToken);

        var principal = await CreateCookiePrincipalAsync(user, cancellationToken);

        await HttpContext.SignInAsync("MyProjectCookie", principal,
            new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

    private async Task<ClaimsPrincipal> CreateCookiePrincipalAsync(CompanyName.ProjectName.Domain.Users.Entities.User user, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity("MyProjectCookie");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(CustomClaimTypes.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false"));

        foreach (var roleName in await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken))
        {
            identity.AddClaim(new Claim("role", roleName));
        }

        return new ClaimsPrincipal(identity);
    }
}
#endif
