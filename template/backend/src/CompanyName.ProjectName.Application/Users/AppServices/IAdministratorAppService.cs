#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Users.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <summary>按作用域读取 Identity 的内置管理员资格。</summary>
public interface IAdministratorAppService : IAppService
{
    /// <summary>查询指定用户是否为当前可用的内置管理员。</summary>
    /// <param name="tenant"><c>host</c> 或租户 Id。</param>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<AdministratorStatusOutputDto> GetForTenantUserAsync(
        string tenant, Guid userId, CancellationToken cancellationToken = default);
}
#endif
