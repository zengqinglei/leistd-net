using CompanyName.ProjectName.Application.Auth.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;
using System.Security.Claims;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthAppService : IAppService
{
    /// <summary>
    /// 验证本地账号并创建会话主体
    /// </summary>
    Task<ClaimsPrincipal> AuthenticateSessionAsync(
        LoginInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户注册
    /// </summary>
    Task<UserOutputDto> RegisterAsync(RegisterInputDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    Task<UserOutputDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新个人信息
    /// </summary>
    Task<UserOutputDto> UpdateCurrentUserAsync(UpdateCurrentUserInputDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改密码
    /// </summary>
    Task ChangePasswordAsync(ChangePasswordInputDto input, CancellationToken cancellationToken = default);
}
