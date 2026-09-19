#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.SignIn;
using CompanyName.ProjectName.Application.Auth.Constants;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Application.Auth.Policies;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Api.Auth;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(
    IAuthAppService authService,
    ICaptchaAppService captchaAppService,
    IEmailVerificationAppService emailVerificationAppService,
    ITenantImpersonationAppService impersonationAppService,
    IUserSessionAppService sessionAppService,
    ITwoFactorAppService twoFactorAppService,
    IUserRegistrationPolicyProvider registrationPolicy,
    IOptions<VerificationCodeOptions> verificationCodeOptions) : BaseController
{
    /// <summary>
    /// 账号密码登录。已启用两步验证时不下发会话，返回第二步凭据
    /// </summary>
    [AllowAnonymous]
    [HttpPost("session-login")]
    [IgnoreAntiforgeryToken]
    public async Task<SessionLoginOutputDto> SessionLoginAsync([FromBody] LoginInputDto request, CancellationToken cancellationToken)
    {
        var result = await authService.AuthenticateSessionAsync(request, cancellationToken);
        return await CompleteSessionLoginAsync(HttpContext, result);
    }

    /// <summary>
    /// 登录第二步：提交验证码或恢复码
    /// </summary>
    [AllowAnonymous]
    [HttpPost("two-factor")]
    [IgnoreAntiforgeryToken]
    public async Task TwoFactorLoginAsync([FromBody] TwoFactorLoginInputDto request, CancellationToken cancellationToken)
    {
        var principal = await authService.CompleteTwoFactorLoginAsync(request, cancellationToken);

        await HttpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>
    /// 按第一步的结果下发会话或第二步凭据。外部登录回调与账号密码登录共用。
    /// </summary>
    internal static async Task<SessionLoginOutputDto> CompleteSessionLoginAsync(HttpContext httpContext, SessionLoginResult result)
    {
        if (result.Principal is null)
        {
            return new SessionLoginOutputDto { RequiresTwoFactor = true, TwoFactorToken = result.TwoFactorToken };
        }

        await httpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, result.Principal,
            new AuthenticationProperties { IsPersistent = true });
        return new SessionLoginOutputDto();
    }

    /// <remarks>
    /// 允许匿名：登出是幂等的 Cookie 清理，不该要求先证明自己有效。挂 [Authorize] 时，
    /// 账号一旦被禁用或锁定，本人反而清不掉服务端 Cookie——登不出去。
    /// 未登录调用同样返回成功，不泄漏"这个会话存不存在"。
    /// </remarks>
    [AllowAnonymous]
    [AllowDuringTwoFactorSetup]
    [HttpPost("logout")]
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        // 先结束服务端会话再删 Cookie：只删 Cookie 的话，被复制走的那份 Cookie 仍然有效
        await sessionAppService.EndCurrentSessionAsync(cancellationToken);
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
    }

    [AllowAnonymous]
    [AllowDuringTwoFactorSetup]
    [HttpGet("security-config")]
    public async Task<SecurityConfigOutputDto> GetSecurityConfigAsync(CancellationToken cancellationToken)
    {
        // 按租户解析：同一套部署下，不同租户的注册门槛可以不同，
        // 而登录页拿到的必须是**它所在那个租户**的那一份。
        var policy = await registrationPolicy.GetAsync(cancellationToken);

        return new SecurityConfigOutputDto
        {
            EnableEmailVerification = policy.EnableEmailVerification,
            EmailVerificationAvailable = verificationCodeOptions.Value.IsKeyUsable
        };
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
    /// 结束模拟登录，会话切回发起人
    /// </summary>
    /// <remarks>
    /// 只要求已认证而不要求 <c>App.Tenants.Impersonation</c>：模拟期间持有的是<b>被模拟者</b>的权限，
    /// 租户管理员没有那条宿主侧权限。要求它会让人退不出去——只能靠退出登录，
    /// 而那等于把"回到自己的账号"变成一次重新登录。
    /// </remarks>
    [Authorize]
    [AllowDuringTwoFactorSetup]
    [HttpPost("end-impersonation")]
    public async Task EndImpersonationAsync(CancellationToken cancellationToken)
    {
        var principal = await impersonationAppService.EndImpersonationAsync(cancellationToken);

        await HttpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>
    /// 当前会话的模拟状态（供界面在顶栏显示模拟提示）
    /// </summary>
    [Authorize]
    [AllowDuringTwoFactorSetup]
    [HttpGet("impersonation")]
    public Task<ImpersonationStatusOutputDto> GetImpersonationStatusAsync(CancellationToken cancellationToken)
        => impersonationAppService.GetStatusAsync(cancellationToken);

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [Authorize]
    [AllowDuringTwoFactorSetup]
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
    /// 设置或清除自己的头像（图片 data URL；为空即清除）
    /// </summary>
    [Authorize]
    [HttpPut("me/avatar")]
    public async Task<UserOutputDto> SetCurrentUserAvatarAsync([FromBody] SetAvatarInputDto request, CancellationToken cancellationToken)
    {
        return await authService.SetCurrentUserAvatarAsync(request, cancellationToken);
    }

    /// <summary>
    /// 给自己当前的邮箱发验证码
    /// </summary>
    [Authorize]
    [HttpPost("me/email-verification")]
    public async Task<EmailVerificationChallengeOutputDto> SendCurrentUserEmailCodeAsync(CancellationToken cancellationToken)
    {
        return await authService.SendCurrentUserEmailCodeAsync(cancellationToken);
    }

    /// <summary>
    /// 用验证码确认自己当前的邮箱
    /// </summary>
    [Authorize]
    [HttpPost("me/email-verification/confirm")]
    public async Task<UserOutputDto> ConfirmCurrentUserEmailAsync([FromBody] EmailVerificationInputDto request, CancellationToken cancellationToken)
    {
        return await authService.ConfirmCurrentUserEmailAsync(request, cancellationToken);
    }

    /// <summary>
    /// 自己的登录设备（仍然有效的会话），当前设备在前
    /// </summary>
    [Authorize]
    [HttpGet("me/sessions")]
    public Task<IReadOnlyList<UserSessionOutputDto>> GetCurrentUserSessionsAsync(CancellationToken cancellationToken)
        => sessionAppService.GetCurrentUserSessionsAsync(cancellationToken);

    /// <summary>
    /// 让自己的某台设备退出登录
    /// </summary>
    [Authorize]
    [HttpDelete("me/sessions/{id:guid}")]
    public Task RevokeCurrentUserSessionAsync(Guid id, CancellationToken cancellationToken)
        => sessionAppService.RevokeCurrentUserSessionAsync(id, cancellationToken);

    /// <summary>
    /// 让除当前设备以外的全部设备退出登录
    /// </summary>
    [Authorize]
    [HttpPost("me/sessions/revoke-others")]
    public Task<int> RevokeOtherCurrentUserSessionsAsync(CancellationToken cancellationToken)
        => sessionAppService.RevokeOtherCurrentUserSessionsAsync(cancellationToken);

    /// <summary>
    /// 自己的两步验证状态
    /// </summary>
    [Authorize]
    [AllowDuringTwoFactorSetup]
    [HttpGet("me/two-factor")]
    public Task<TwoFactorStatusOutputDto> GetTwoFactorStatusAsync(CancellationToken cancellationToken)
        => twoFactorAppService.GetStatusAsync(cancellationToken);

    /// <summary>
    /// 开始设置两步验证：生成待启用的密钥
    /// </summary>
    [Authorize]
    [AllowDuringTwoFactorSetup]
    [HttpPost("me/two-factor/setup")]
    public Task<TwoFactorSetupOutputDto> BeginTwoFactorSetupAsync(CancellationToken cancellationToken)
        => twoFactorAppService.BeginSetupAsync(cancellationToken);

    /// <summary>
    /// 用验证码确认并启用两步验证，返回恢复码
    /// </summary>
    /// <remarks>受限会话（租户要求两步验证而此前未启用）启用成功后换发一个不受限的会话。</remarks>
    [Authorize]
    [AllowDuringTwoFactorSetup]
    [HttpPost("me/two-factor/enable")]
    public async Task<TwoFactorRecoveryCodesOutputDto> EnableTwoFactorAsync(
        [FromBody] TwoFactorCodeInputDto request,
        CancellationToken cancellationToken)
    {
        var output = await twoFactorAppService.EnableAsync(request, cancellationToken);

        if (User.HasClaim(claim => claim.Type == TwoFactorClaimTypes.SetupRequired))
        {
            var principal = await authService.ReissueSessionAsync(cancellationToken);
            await HttpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, principal,
                new AuthenticationProperties { IsPersistent = true });
        }

        return output;
    }

    /// <summary>
    /// 停用两步验证（要密码与验证码）
    /// </summary>
    [Authorize]
    [HttpPost("me/two-factor/disable")]
    public Task DisableTwoFactorAsync([FromBody] DisableTwoFactorInputDto request, CancellationToken cancellationToken)
        => twoFactorAppService.DisableAsync(request, cancellationToken);

    /// <summary>
    /// 重新生成恢复码（要验证码）
    /// </summary>
    [Authorize]
    [HttpPost("me/two-factor/recovery-codes")]
    public Task<TwoFactorRecoveryCodesOutputDto> RegenerateRecoveryCodesAsync(
        [FromBody] TwoFactorCodeInputDto request,
        CancellationToken cancellationToken)
        => twoFactorAppService.RegenerateRecoveryCodesAsync(request, cancellationToken);

    /// <summary>
    /// 修改密码（其他设备随之退出登录）
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task ChangePasswordAsync([FromBody] ChangePasswordInputDto request, CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(request, cancellationToken);
    }
}
#endif
