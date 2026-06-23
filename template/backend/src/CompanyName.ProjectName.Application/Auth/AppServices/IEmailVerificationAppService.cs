using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public interface IEmailVerificationAppService : IAppService
{
    /// <summary>
    /// 发送邮箱验证码
    /// </summary>
    Task SendEmailCodeAsync(SendEmailCodeInputDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证邮箱验证码
    /// </summary>
    Task<bool> ValidateEmailCodeAsync(string email, string code, CancellationToken cancellationToken = default);
}
