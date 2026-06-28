#if (IncludeIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务接口
/// </summary>
public interface IExternalAuthAppService : IAppService
{
    /// <summary>
    /// 获取外部登录 URL
    /// </summary>
    ExternalLoginUrlOutputDto GetLoginUrl(string provider);

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    Task<User> AuthenticateExternalUserAsync(string provider, ExternalLoginCallbackInputDto request, CancellationToken cancellationToken = default);
}
#endif
