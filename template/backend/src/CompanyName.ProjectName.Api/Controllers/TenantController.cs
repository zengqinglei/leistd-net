using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 租户管理（宿主侧能力：权限声明为 Host 侧别，租户上下文内任何主体都无法通过检查）
/// </summary>
[Authorize]
[Route("api/v1/tenants")]
public sealed class TenantController(ITenantAppService tenantAppService) : BaseController
{
    /// <summary>
    /// 分页查询租户
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstant.Tenants.Default)]
    public Task<PagedResultDto<TenantOutputDto>> GetPagedAsync(
        [FromQuery] GetTenantPagedInputDto input,
        CancellationToken cancellationToken)
        => tenantAppService.GetPagedAsync(input, cancellationToken);

    /// <summary>
    /// 获取租户详情
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Default)]
    public Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken)
        => tenantAppService.GetAsync(id, cancellationToken);

    /// <summary>
    /// 创建租户（含租户管理员初始凭据，创建后立即在租内种子）
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstant.Tenants.Create)]
    public Task<TenantOutputDto> CreateAsync(
        [FromBody] CreateTenantInputDto input,
        CancellationToken cancellationToken)
        => tenantAppService.CreateAsync(input, cancellationToken);

    /// <summary>
    /// 更新租户名称与显示名
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantOutputDto> UpdateAsync(
        Guid id,
        [FromBody] UpdateTenantInputDto input,
        CancellationToken cancellationToken)
        => tenantAppService.UpdateAsync(id, input, cancellationToken);

    /// <summary>
    /// 启用/停用租户（停用后该租户请求自下一次校验起 403）
    /// </summary>
    [HttpPut("{id:guid}/activation")]
    [Authorize(Policy = PermissionConstant.Tenants.Update)]
    public Task<TenantOutputDto> SetActivationAsync(
        Guid id,
        [FromBody] UpdateTenantActivationInputDto input,
        CancellationToken cancellationToken)
        => tenantAppService.SetActivationAsync(id, input, cancellationToken);

    /// <summary>
    /// 删除租户（软删除）
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstant.Tenants.Delete)]
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        => tenantAppService.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// 登录前按名称探测租户（匿名）：前端登录页租户选择的数据源，只回最小信息
    /// </summary>
    [AllowAnonymous]
    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<TenantLookupOutputDto>> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var tenant = await tenantAppService.FindByNameAsync(name, cancellationToken);
        return tenant is null ? NotFound() : tenant;
    }
}
