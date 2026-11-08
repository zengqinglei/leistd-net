using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.SignIn;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>认证服务接口。</summary>
public interface IAuthAppService : IAppService
{
    /// <summary>验证本地账号：直接得到会话主体，或（有能用的第二步方式时）得到待完成的登录与可用方式。</summary>
    Task<SessionLoginResult> AuthenticateSessionAsync(
        LoginInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>登录第二步：校验其中一种方式的凭证（通行密钥、身份验证器、邮箱或恢复码）并创建会话主体。</summary>
    /// <param name="challengeToken">临时 Cookie 里的挑战引用；没有时按已过期拒绝（<c>Auth:TwoFactorChallengeExpired</c>），下同。</param>
    /// <param name="input">这一步出示的凭证。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ClaimsPrincipal> CompleteTwoFactorLoginAsync(
        string? challengeToken,
        TwoFactorLoginInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>登录第二步：给出用通行密钥验证的断言选项；这种方式此刻不能用时以 <c>Auth:TwoFactorMethodUnavailable</c> 拒绝。</summary>
    Task<PasskeyOptionsOutputDto> BeginTwoFactorPasskeyAsync(
        string? challengeToken,
        CancellationToken cancellationToken = default);

    /// <summary>读出待完成的登录，在挑战自己的租户里核对；已过期、不存在或已失效时返回 null 并作废挑战。</summary>
    Task<TwoFactorPrompt?> FindTwoFactorLoginAsync(
        string? challengeToken,
        CancellationToken cancellationToken = default);

    /// <summary>登录第二步的上下文（可用方式、默认方式、打码邮箱），供第二步页刷新后继续。</summary>
    Task<TwoFactorLoginOutputDto> GetTwoFactorLoginAsync(
        string? challengeToken,
        CancellationToken cancellationToken = default);

    /// <summary>放弃待完成的登录：作废挑战。幂等。</summary>
    Task CancelTwoFactorLoginAsync(
        string? challengeToken,
        CancellationToken cancellationToken = default);

    /// <summary>免口令登录：给出断言选项与仪式凭据。</summary>
    Task<PasskeyOptionsOutputDto> BeginPasskeySignInAsync(CancellationToken cancellationToken = default);

    /// <summary>免口令登录：校验断言并创建会话主体，不再要求第二步。</summary>
    Task<ClaimsPrincipal> CompletePasskeySignInAsync(
        PasskeySignInInputDto input,
        CancellationToken cancellationToken = default);
#if (Email)

    /// <summary>登录第二步：把验证码发到本人的已验证邮箱；邮箱方式此刻不能用时以 <c>Auth:TwoFactorMethodUnavailable</c> 拒绝，
    /// 待完成的登录剩余不足一个码的有效期时不新发（<c>Auth:EmailCodeResendTooLate</c>），登录照常可以用已发的码或其他方式完成。</summary>
    Task<TwoFactorEmailCodeOutputDto> SendTwoFactorEmailCodeAsync(
        string? challengeToken,
        CancellationToken cancellationToken = default);
#endif

    /// <summary>当前租户的注册摘要：是否开放自助注册、注册时是否要求邮箱验证、部署能否发出验证码。匿名可读。</summary>
    Task<SecurityConfigOutputDto> GetSecurityConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>用户注册；租户关闭自助注册时拒绝。</summary>
    Task<UserOutputDto> RegisterAsync(RegisterInputDto input, CancellationToken cancellationToken = default);

    /// <summary>获取当前用户信息。</summary>
    Task<UserOutputDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>更新个人信息。</summary>
    Task<UserOutputDto> UpdateCurrentUserAsync(UpdateCurrentUserInputDto input, CancellationToken cancellationToken = default);

    /// <summary>设置或清除自己的头像（只接受图片）。</summary>
    Task<UserOutputDto> SetCurrentUserAvatarAsync(SetAvatarInputDto input, CancellationToken cancellationToken = default);
#if (Email)

    /// <summary>给自己当前的邮箱发验证码。</summary>
    Task<EmailVerificationChallengeOutputDto> SendCurrentUserEmailCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>用验证码确认自己当前的邮箱。</summary>
    Task<UserOutputDto> ConfirmCurrentUserEmailAsync(EmailVerificationInputDto input, CancellationToken cancellationToken = default);
#endif

    /// <summary>修改密码。成功后撤销本人除当前会话以外的全部会话。</summary>
    Task ChangePasswordAsync(ChangePasswordInputDto input, CancellationToken cancellationToken = default);
#if (ExternalLogin)

    /// <summary>给没有本地口令的账号设置第一个口令。成功后撤销本人除当前会话以外的全部会话。</summary>
    /// <remarks>已有口令时以 <c>Security:PasswordAlreadySet</c> 拒绝；会话不够新时要出示验证码，没开两步验证的要重新登录。</remarks>
    Task SetPasswordAsync(SetPasswordInputDto input, CancellationToken cancellationToken = default);
#endif
}
