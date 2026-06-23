#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Ddd.Application.Permission;
#endif
using CompanyName.ProjectName.Application.Users.AppServices;
using CompanyName.ProjectName.Application.Users.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 用户管理控制器
/// </summary>
[Authorize]
[Route("api/v1/users")]
public class UserController(IUserAppService userAppService) : BaseController
{
    /// <summary>
    /// 获取用户列表（需要用户查看权限）
    /// </summary>
    [HttpGet]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Default)]
#endif
    public async Task<PagedResultDto<UserManagementOutputDto>> GetPagedListAsync(
        [FromQuery] GetUserPagedInputDto input,
        CancellationToken cancellationToken)
    {
        return await userAppService.GetPagedListAsync(input, cancellationToken);
    }

    /// <summary>
    /// 获取用户详情（需要用户查看权限）
    /// </summary>
    [HttpGet("{id}")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Default)]
#endif
    public async Task<UserManagementOutputDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await userAppService.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// 创建用户（需要用户创建权限）
    /// </summary>
    [HttpPost]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Create)]
#endif
    public async Task<UserManagementOutputDto> CreateAsync(
        [FromBody] CreateUserInputDto input,
        CancellationToken cancellationToken)
    {
        return await userAppService.CreateAsync(input, cancellationToken);
    }

    /// <summary>
    /// 更新用户（需要用户更新权限）
    /// </summary>
    [HttpPut("{id}")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Update)]
#endif
    public async Task<UserManagementOutputDto> UpdateAsync(
        Guid id,
        [FromBody] UpdateUserInputDto input,
        CancellationToken cancellationToken)
    {
        return await userAppService.UpdateAsync(id, input, cancellationToken);
    }

    /// <summary>
    /// 启用用户（需要用户更新权限）
    /// </summary>
    [HttpPatch("{id}/enable")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Update)]
#endif
    public async Task EnableAsync(Guid id, CancellationToken cancellationToken)
    {
        await userAppService.EnableAsync(id, cancellationToken);
    }

    /// <summary>
    /// 禁用用户（需要用户更新权限）
    /// </summary>
    [HttpPatch("{id}/disable")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Update)]
#endif
    public async Task DisableAsync(Guid id, CancellationToken cancellationToken)
    {
        await userAppService.DisableAsync(id, cancellationToken);
    }

    /// <summary>
    /// 重置用户密码（需要用户更新权限）
    /// </summary>
#if (IncludeIdentity)
    [HttpPost("{id}/reset-password")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Update)]
#endif
    public async Task ResetPasswordAsync(
        Guid id,
        [FromBody] ResetUserPasswordInputDto input,
        CancellationToken cancellationToken)
    {
        await userAppService.ResetPasswordAsync(id, input, cancellationToken);
    }
#endif

    /// <summary>
    /// 删除用户（需要用户删除权限）
    /// </summary>
    [HttpDelete("{id}")]
#if (IncludeRoles)
    [Permission(PermissionConstant.Users.Delete)]
#endif
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await userAppService.DeleteAsync(id, cancellationToken);
    }
}
