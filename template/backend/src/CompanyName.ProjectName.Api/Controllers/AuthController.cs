#if (IncludeIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using Leistd.Ddd.Domain.Repositories;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Options;
using Leistd.Exception.Core;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[Route("api/v1/auth")]
public class AuthController(
    IAuthAppService authService,
    ICaptchaAppService captchaAppService,
    IEmailVerificationAppService emailVerificationAppService,
    UserDomainService userDomainService,
    IRepository<User, Guid> userRepository,
    IOptions<UserRegistrationOptions> securityOptions) : BaseController
{
    [AllowAnonymous]
    [HttpPost("session-login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SessionLoginAsync([FromBody] LoginInputDto request, CancellationToken cancellationToken)
    {
        // 验证凭据
        var user = await userDomainService.ValidateCredentialsAsync(
            request.UsernameOrEmail,
            request.Password,
            cancellationToken);

        if (user == null)
        {
            throw new UnauthorizedException($"登录失败: 用户不存在或密码错误 - {request.UsernameOrEmail}");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException($"登录失败: 用户已被禁用 - 用户: {user.Username}");
        }

        if (user.IsLockedOut())
        {
            throw new UnauthorizedException($"登录失败: 用户已被锁定 - 用户: {user.Username}, 锁定至: {user.LockoutEnd}");
        }

        // 记录登录成功并建立 Cookie 会话
        user.RecordLoginSuccess();
        await userRepository.UpdateAsync(user, cancellationToken);

        var principal = await CreateCookiePrincipalAsync(user, cancellationToken);

        await HttpContext.SignInAsync("MyProjectCookie", principal,
            new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

    private async Task<ClaimsPrincipal> CreateCookiePrincipalAsync(User user, CancellationToken cancellationToken)
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

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync("MyProjectCookie");
        return NoContent();
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
    public async Task SendEmailCodeAsync([FromBody] SendEmailCodeInputDto request, CancellationToken cancellationToken)
    {
        await emailVerificationAppService.SendEmailCodeAsync(request, cancellationToken);
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
