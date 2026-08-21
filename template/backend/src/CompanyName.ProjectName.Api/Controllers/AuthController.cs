#if (IdentityService)
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
    public async Task SessionLoginAsync([FromBody] LoginInputDto request, CancellationToken cancellationToken)
    {
        // 验证凭据
        var user = await userDomainService.ValidateCredentialsAsync(
            request.UsernameOrEmail,
            request.Password,
            cancellationToken);

        if (user == null)
        {
            throw new UnauthorizedException($"Login failed: user not found or incorrect password - {request.UsernameOrEmail}")
#if (IncludeLocalization)
                .WithLocalization("Auth:InvalidCredentials")
                .WithData("UsernameOrEmail", request.UsernameOrEmail)
#endif
                ;
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException($"Login failed: user is disabled - user: {user.Username}")
#if (IncludeLocalization)
                .WithLocalization("Auth:UserDisabled")
                .WithData("Username", user.Username)
#endif
                ;
        }

        if (user.IsLockedOut())
        {
            throw new UnauthorizedException($"Login failed: user is locked out - user: {user.Username}, locked until: {user.LockoutEnd}")
#if (IncludeLocalization)
                .WithLocalization("Auth:UserLockedOut")
                .WithData("Username", user.Username)
                .WithData("LockoutEnd", user.LockoutEnd)
#endif
                ;
        }

        // 记录登录成功并建立 Cookie 会话
        user.RecordLoginSuccess();
        await userRepository.UpdateAsync(user, cancellationToken);

        var principal = await CreateCookiePrincipalAsync(user, cancellationToken);

        await HttpContext.SignInAsync("MyProjectCookie", principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    private async Task<ClaimsPrincipal> CreateCookiePrincipalAsync(User user, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity("MyProjectCookie");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(CustomClaimTypes.IsSuperAdmin, user.IsSuperAdmin ? "true" : "false"));
#if (MultiTenancy)
        // 租户 claim：多租户解析链以它定案已登录用户的租户，请求头无法改写
        if (user.TenantId is { } tenantId)
        {
            identity.AddClaim(new Claim(CustomClaimTypes.TenantId, tenantId.ToString()));
        }
#endif

#if (LocalAuthorization)
        foreach (var roleName in await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken))
        {
            identity.AddClaim(new Claim("role", roleName));
        }
#endif

        return new ClaimsPrincipal(identity);
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
        await HttpContext.SignOutAsync("MyProjectCookie");
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
