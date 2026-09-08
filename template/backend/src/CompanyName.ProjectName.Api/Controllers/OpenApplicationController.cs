#if (LocalIdentity)
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 开放应用管理控制器
/// </summary>
[Authorize]
[Route("api/v1/open-applications")]
public sealed class OpenApplicationController(IOpenApplicationAppService openApplicationAppService) : BaseController
{
    /// <summary>
    /// 获取开放应用列表
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstant.OpenApplications.Default)]
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
    [Authorize(Policy = PermissionConstant.OpenApplications.Default)]
    public async Task<OpenApplicationOutputDto> GetAsync(string id, CancellationToken cancellationToken)
    {
        return await openApplicationAppService.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// 创建开放应用
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstant.OpenApplications.Create)]
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
    [Authorize(Policy = PermissionConstant.OpenApplications.Update)]
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
    [Authorize(Policy = PermissionConstant.OpenApplications.Delete)]
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await openApplicationAppService.DeleteAsync(id, cancellationToken);
    }

    /// <summary>
    /// 重置开放应用密钥
    /// </summary>
    [HttpPost("{id}/reset-secret")]
    [Authorize(Policy = PermissionConstant.OpenApplications.ResetSecret)]
    public async Task<ResetOpenApplicationSecretOutputDto> ResetSecretAsync(
        string id,
        CancellationToken cancellationToken)
    {
        return await openApplicationAppService.ResetSecretAsync(id, cancellationToken);
    }
}
#endif
