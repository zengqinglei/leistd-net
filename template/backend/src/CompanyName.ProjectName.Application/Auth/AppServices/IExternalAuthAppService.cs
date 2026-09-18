#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;
using CompanyName.ProjectName.Application.Auth.SignIn;

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
    /// 处理外部登录回调：直接得到会话主体，或（已启用两步验证时）得到第二步凭据
    /// </summary>
    Task<SessionLoginResult> AuthenticateExternalUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 本人的外部账号绑定情况
    /// </summary>
    Task<ExternalLoginsOutputDto> GetCurrentUserExternalLoginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 把外部身份绑定到当前用户；该外部账号已绑在别人名下时拒绝
    /// </summary>
    Task LinkCurrentUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 解绑当前用户的一个外部账号；必须还剩一种登录方式
    /// </summary>
    Task UnlinkCurrentUserAsync(Guid id, CancellationToken cancellationToken = default);
}
#endif
