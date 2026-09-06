#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;
using System.Security.Claims;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务接口
/// </summary>
public interface IExternalAuthAppService : IAppService
{
    /// <summary>
    /// 获取外部登录 URL
    /// </summary>
    ExternalLoginUrlOutputDto GetLoginUrl(string provider, string state);

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    Task<ClaimsPrincipal> AuthenticateExternalUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default);
}
#endif
