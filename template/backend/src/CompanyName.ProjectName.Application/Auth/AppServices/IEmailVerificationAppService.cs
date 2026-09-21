using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public interface IEmailVerificationAppService : IAppService
{
    /// <summary>
    /// 发送邮箱验证码
    /// </summary>
    Task<EmailVerificationChallengeOutputDto> SendEmailCodeAsync(
        SendEmailCodeInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证邮箱验证码
    /// </summary>
    Task<bool> ValidateEmailChallengeAsync(
        string email,
        EmailVerificationInputDto verification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 给已登录用户当前的邮箱发送验证码（验证已有账号的邮箱，不是注册）
    /// </summary>
    Task<EmailVerificationChallengeOutputDto> SendAccountEmailCodeAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验已登录用户邮箱的验证码；注册时发出的验证码不能用于此处
    /// </summary>
    Task<bool> ValidateAccountEmailChallengeAsync(
        string email,
        EmailVerificationInputDto verification,
        CancellationToken cancellationToken = default);
}
