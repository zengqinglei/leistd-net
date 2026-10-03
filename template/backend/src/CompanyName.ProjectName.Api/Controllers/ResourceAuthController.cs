#if (RemoteTokenAuth)
using CompanyName.ProjectName.Application.Shared;
using Leistd.Security.Users;
using Leistd.MultiTenancy.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

[Route("api/v1/auth")]
public sealed class ResourceAuthController(ICurrentUser currentUser, ICurrentTenant currentTenant) : BaseController
{
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string returnUrl = "/workspace") => Challenge(new AuthenticationProperties
    {
        RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/workspace"
    }, AuthenticationSchemeNames.OpenIdConnect);

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = currentUser.Id, username = currentUser.Username ?? "", email = currentUser.Email ?? "",
        displayName = currentUser.Name,
        isEmailVerified = User.FindFirst("email_verified")?.Value == "true",
        isSuperAdmin = User.FindFirst("is_super_admin")?.Value == "true",
        roles = User.FindAll("role").Select(claim => claim.Value).ToArray(), tenantId = currentTenant.Id
    });

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        // 本地会话单独退出且不带回跳地址：Cookie 处理器见到回跳地址就写 302，会盖过官方退出表单（FormPost 不改状态码）
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, AuthenticationSchemeNames.OpenIdConnect);
    }
}
#endif
