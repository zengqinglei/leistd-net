#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(
    IAuthAppService authService,
    ICaptchaAppService captchaAppService,
    IEmailVerificationAppService emailVerificationAppService,
    IOptions<UserRegistrationOptions> securityOptions) : BaseController
{
    [AllowAnonymous]
    [HttpPost("session-login")]
    [IgnoreAntiforgeryToken]
    public async Task SessionLoginAsync([FromBody] LoginInputDto request, CancellationToken cancellationToken)
    {
        var principal = await authService.AuthenticateSessionAsync(request, cancellationToken);

        await HttpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <remarks>
    /// 允许匿名：登出是幂等的 Cookie 清理，不该要求先证明自己有效。挂 [Authorize] 时，
    /// 账号一旦被禁用或锁定，本人反而清不掉服务端 Cookie——登不出去。
    /// 未登录调用同样返回成功，不泄漏"这个会话存不存在"。
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task LogoutAsync()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
    }

    [AllowAnonymous]
    [HttpGet("security-config")]
    public Task<SecurityConfigOutputDto> GetSecurityConfigAsync()
    {
        return Task.FromResult(new SecurityConfigOutputDto
        {
            EnableEmailVerification = securityOptions.Value.EnableEmailVerification
        });
    }

    [AllowAnonymous]
    [HttpGet("captcha")]
    public async Task<CaptchaOutputDto> GetCaptchaAsync(CancellationToken cancellationToken)
    {
        return await captchaAppService.GenerateCaptchaAsync(cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("send-email-code")]
    public async Task<EmailVerificationChallengeOutputDto> SendEmailCodeAsync(
        [FromBody] SendEmailCodeInputDto request,
        CancellationToken cancellationToken)
    {
        return await emailVerificationAppService.SendEmailCodeAsync(request, cancellationToken);
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<UserOutputDto> RegisterAsync([FromBody] RegisterInputDto request, CancellationToken cancellationToken)
    {
        return await authService.RegisterAsync(request, cancellationToken);
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<UserOutputDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        return await authService.GetCurrentUserAsync(cancellationToken);
    }

    /// <summary>
    /// 更新个人信息
    /// </summary>
    [Authorize]
    [HttpPut("me")]
    public async Task<UserOutputDto> UpdateCurrentUserAsync([FromBody] UpdateCurrentUserInputDto request, CancellationToken cancellationToken)
    {
        return await authService.UpdateCurrentUserAsync(request, cancellationToken);
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task ChangePasswordAsync([FromBody] ChangePasswordInputDto request, CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(request, cancellationToken);
    }
}
#endif
