using CompanyName.ProjectName.Application.Settings.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <summary>
/// 设置读写应用服务
/// </summary>
public interface ISettingAppService : IAppService
{
    /// <summary>获取当前用户可见设置在各层级上的覆盖值。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SettingOutputDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>写入当前用户自己的偏好。</summary>
    /// <param name="input">要写入的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetForCurrentUserAsync(SetSettingInputDto input, CancellationToken cancellationToken = default);

    /// <summary>写入当前租户的默认值，需要设置管理权限。</summary>
    /// <param name="input">要写入的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetForCurrentTenantAsync(SetSettingInputDto input, CancellationToken cancellationToken = default);
}
