using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.MultiTenancy.Resolution;
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

    /// <summary>
    /// 按当前请求的主机名探测租户（匿名）：子域名部署下让登录页把租户显示成只读，
    /// 用户不必手敲租户名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只按<b>主机名</b>解析，刻意不接受用户名之类的入参：那会需要一个匿名的
    /// 「这个账号属于哪些租户」查询，同时泄露账号是否存在与租户拓扑，一份邮箱字典就能刷出来。
    /// 主机名是调用方自己带来的，回显它不泄露任何新信息。
    /// </para>
    /// <para>
    /// 复用框架的 <see cref="DomainTenantResolveContributor"/> 而不是在这里重写主机名匹配：
    /// 两处各写一份，受管域判定与"域内但非租户"这类分支迟早不一致。
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("by-host")]
    public async Task<TenantByHostOutputDto> FindByHostAsync(
        [FromServices] IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var context = new TenantResolveContext(serviceProvider);
        await new DomainTenantResolveContributor().ResolveAsync(context);

        if (context.TenantIdOrName is { Length: > 0 } tenantName)
        {
            var tenant = await tenantAppService.FindByNameAsync(tenantName, cancellationToken);
            return new TenantByHostOutputDto
            {
                Decision = HostTenantDecision.Tenant,
                Tenant = tenant
            };
        }

        // context.Handled 是框架给"受管域内但不指向租户"的标记，必须与"根本不是受管域"分开回传：
        // 两者都讲成"没有租户"时，界面会在宿主域上继续显示上次记住的那个租户，
        // 而服务端此刻已按宿主处理请求——显示的和生效的不是同一个租户上下文。
        return new TenantByHostOutputDto
        {
            Decision = context.Handled ? HostTenantDecision.Host : HostTenantDecision.Undecided
        };
    }
}
