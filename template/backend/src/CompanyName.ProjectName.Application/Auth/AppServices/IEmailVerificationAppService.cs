using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public interface IEmailVerificationAppService : IAppService
{
    /// <summary>
    /// 当前租户的注册验证配置：注册时是否要求邮箱验证，以及部署能否发出验证码
    /// </summary>
    Task<SecurityConfigOutputDto> GetSecurityConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送邮箱验证码
    /// </summary>
    Task<EmailVerificationChallengeOutputDto> SendEmailCodeAsync(
        SendEmailCodeInputDto input,
        CancellationToken cancellationToken = default);
}
