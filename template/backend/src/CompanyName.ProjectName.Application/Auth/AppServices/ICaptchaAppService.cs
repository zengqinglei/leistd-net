using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public interface ICaptchaAppService : IAppService
{
    /// <summary>
    /// 生成图形验证码
    /// </summary>
    Task<CaptchaOutputDto> GenerateCaptchaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证图形验证码
    /// </summary>
    Task<bool> ValidateCaptchaAsync(string token, string code, CancellationToken cancellationToken = default);
}
