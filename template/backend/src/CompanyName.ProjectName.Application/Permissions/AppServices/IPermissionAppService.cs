using CompanyName.ProjectName.Application.Permissions.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.Permissions.AppServices;

/// <summary>
/// 权限管理应用服务接口
/// </summary>
public interface IPermissionAppService : IAppService
{
    /// <summary>
    /// 获取当前登录用户的有效权限与授权版本。
    /// </summary>
    Task<CurrentPermissionsOutputDto> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取全部权限定义，按组分区、按父子成树。
    /// </summary>
    Task<IReadOnlyList<PermissionDefinitionGroupOutputDto>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取某个主体（用户或角色）的授予状态。
    /// </summary>
    Task<PermissionGrantsOutputDto> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子替换某个主体的全部授予。
    /// </summary>
    Task<PermissionGrantsOutputDto> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken = default);
}
