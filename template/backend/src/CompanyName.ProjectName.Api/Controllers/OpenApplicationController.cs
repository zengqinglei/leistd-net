#if (IncludeIdentity)
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Provider;
#endif
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 开放应用管理控制器
/// </summary>
[Authorize]
[Route("api/v1/open-applications")]
public class OpenApplicationController(IOpenApplicationAppService openApplicationAppService) : BaseController
{
    /// <summary>
    /// 获取开放应用列表
    /// </summary>
    [HttpGet]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.Default)]
#endif
    public async Task<PagedResultDto<OpenApplicationOutputDto>> GetPagedListAsync(
        [FromQuery] GetOpenApplicationPagedInputDto input,
        CancellationToken cancellationToken)
    {
        return await openApplicationAppService.GetPagedListAsync(input, cancellationToken);
    }

    /// <summary>
    /// 获取开放应用详情
    /// </summary>
    [HttpGet("{id}")]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.Default)]
#endif
    public async Task<OpenApplicationOutputDto> GetAsync(string id, CancellationToken cancellationToken)
    {
        return await openApplicationAppService.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// 创建开放应用
    /// </summary>
    [HttpPost]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.Create)]
#endif
    public async Task<OpenApplicationOutputDto> CreateAsync(
        [FromBody] CreateOpenApplicationInputDto input,
        CancellationToken cancellationToken)
    {
        return await openApplicationAppService.CreateAsync(input, cancellationToken);
    }

    /// <summary>
    /// 更新开放应用
    /// </summary>
    [HttpPut("{id}")]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.Update)]
#endif
    public async Task<OpenApplicationOutputDto> UpdateAsync(
        string id,
        [FromBody] UpdateOpenApplicationInputDto input,
        CancellationToken cancellationToken)
    {
        return await openApplicationAppService.UpdateAsync(id, input, cancellationToken);
    }

    /// <summary>
    /// 删除开放应用
    /// </summary>
    [HttpDelete("{id}")]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.Delete)]
#endif
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await openApplicationAppService.DeleteAsync(id, cancellationToken);
    }

    /// <summary>
    /// 重置开放应用密钥
    /// </summary>
    [HttpPost("{id}/reset-secret")]
#if (IncludeRoles)
    [Authorize(Policy = PermissionConstant.OpenApplications.ResetSecret)]
#endif
    public async Task<ResetOpenApplicationSecretOutputDto> ResetSecretAsync(
        string id,
        CancellationToken cancellationToken)
    {
        return await openApplicationAppService.ResetSecretAsync(id, cancellationToken);
    }
}
#endif
