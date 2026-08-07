#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.AppServices;
using CompanyName.ProjectName.Application.Permissions.Dtos;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 权限管理控制器
/// </summary>
/// <remarks>
/// 与 OpenIddict 的 <see cref="ConnectController"/> 无关：后者负责 OAuth2/OIDC 协议端点，
/// 本控制器负责功能权限的定义查询与授予管理。
/// </remarks>
[Authorize]
[Route("api/v1/permissions")]
public class PermissionController(IPermissionAppService permissionAppService) : BaseController
{
    /// <summary>
    /// 获取当前用户的有效权限与授权版本（仅要求已认证）
    /// </summary>
    [HttpGet("current")]
    public async Task<CurrentPermissionsOutputDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        return await permissionAppService.GetCurrentAsync(cancellationToken);
    }

    /// <summary>
    /// 获取权限定义树（需要权限定义查看权限）
    /// </summary>
    [HttpGet("definitions")]
    [Authorize(Policy = PermissionConstant.Permissions.Default)]
    public async Task<IReadOnlyList<PermissionDefinitionGroupOutputDto>> GetDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        return await permissionAppService.GetDefinitionsAsync(cancellationToken);
    }

    /// <summary>
    /// 获取角色的权限授予（需要角色权限配置权限）
    /// </summary>
    [HttpGet("grants/roles/{roleId}")]
    [Authorize(Policy = PermissionConstant.Roles.ManagePermissions)]
    public async Task<PermissionGrantsOutputDto> GetRoleGrantsAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        return await permissionAppService.GetGrantsAsync(
            PermissionGrantProviderNames.Role,
            roleId.ToString(),
            cancellationToken);
    }

    /// <summary>
    /// 替换角色的权限授予（需要角色权限配置权限）
    /// </summary>
    [HttpPut("grants/roles/{roleId}")]
    [Authorize(Policy = PermissionConstant.Roles.ManagePermissions)]
    public async Task<PermissionGrantsOutputDto> ReplaceRoleGrantsAsync(
        Guid roleId,
        [FromBody] ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken)
    {
        return await permissionAppService.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            roleId.ToString(),
            input,
            cancellationToken);
    }

    /// <summary>
    /// 获取用户的权限例外（需要用户权限配置权限）
    /// </summary>
    [HttpGet("grants/users/{userId}")]
    [Authorize(Policy = PermissionConstant.Users.ManagePermissions)]
    public async Task<PermissionGrantsOutputDto> GetUserGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await permissionAppService.GetGrantsAsync(
            PermissionGrantProviderNames.User,
            userId.ToString(),
            cancellationToken);
    }

    /// <summary>
    /// 替换用户的权限例外（需要用户权限配置权限）
    /// </summary>
    [HttpPut("grants/users/{userId}")]
    [Authorize(Policy = PermissionConstant.Users.ManagePermissions)]
    public async Task<PermissionGrantsOutputDto> ReplaceUserGrantsAsync(
        Guid userId,
        [FromBody] ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken)
    {
        return await permissionAppService.ReplaceGrantsAsync(
            PermissionGrantProviderNames.User,
            userId.ToString(),
            input,
            cancellationToken);
    }
}
#endif
