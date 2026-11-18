#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Users.AppServices;
using CompanyName.ProjectName.Application.Users.Constants;
using CompanyName.ProjectName.Application.Users.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>资源服务首次建立本地成员时，以机器身份查询指定用户的内置管理员资格。</summary>
[Route("api/v1/internal/administrators")]
public sealed class InternalAdministratorController(IAdministratorAppService administratorAppService) : BaseController
{
    /// <summary>读取指定作用域内的内置管理员资格。租户不存在或已停用时返回 404。</summary>
    /// <param name="tenant"><c>host</c> 或租户 Id。</param>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    [Authorize(Policy = AdministratorPolicies.Read)]
    [HttpGet("{tenant}/{userId:guid}")]
    public async Task<AdministratorStatusOutputDto> GetAsync(string tenant, Guid userId, CancellationToken cancellationToken)
    {
        return await administratorAppService.GetForTenantUserAsync(tenant, userId, cancellationToken);
    }
}
#endif
