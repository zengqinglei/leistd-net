using CompanyName.ProjectName.Application.Settings.AppServices;
using CompanyName.ProjectName.Application.Settings.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 设置中心：按层级读取覆盖值，按用户或按租户写入。
/// </summary>
/// <remarks>
/// 写入分成两个端点而不是一个带 scope 参数的端点：两者的授权要求不同——
/// 改自己的偏好只需登录，改租户默认值需要设置管理权限。合成一个端点会让这层差异
/// 藏进请求体，路由上看不出来。
/// </remarks>
[Authorize]
[Route("api/v1/settings")]
public sealed class SettingController(ISettingAppService settingAppService) : BaseController
{
    /// <summary>获取各层级的设置覆盖值（用户级、租户级与代码默认值分别给出）。</summary>
    [HttpGet]
    public Task<IReadOnlyList<SettingOutputDto>> GetAsync(CancellationToken cancellationToken = default)
        => settingAppService.GetAsync(cancellationToken);

    /// <summary>写入当前用户自己的偏好；<c>value</c> 为空表示恢复为上一层的值。</summary>
    [HttpPut("current-user")]
    public Task SetForCurrentUserAsync(
        [FromBody] SetSettingInputDto input,
        CancellationToken cancellationToken = default)
        => settingAppService.SetForCurrentUserAsync(input, cancellationToken);

    /// <summary>写入当前租户的默认值，需要设置管理权限。</summary>
    [HttpPut("current-tenant")]
    public Task SetForCurrentTenantAsync(
        [FromBody] SetSettingInputDto input,
        CancellationToken cancellationToken = default)
        => settingAppService.SetForCurrentTenantAsync(input, cancellationToken);
}
