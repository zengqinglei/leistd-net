#if (LocalAuthorization)
using System.Linq.Dynamic.Core;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Roles.Mappings;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Leistd.ObjectMapping.Core;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Roles.AppServices;

/// <summary>
/// 角色应用服务
/// </summary>
public class RoleAppService(
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
    IObjectMapper objectMapper,
    ILogger<RoleAppService> logger,
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IRoleAppService
{
    public async Task<PagedResultDto<RoleOutputDto>> GetPagedListAsync(
        GetRolePagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var query = await roleRepository.GetQueryableAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            query = query.Where(r => r.Name.Contains(keyword) || r.DisplayName.Contains(keyword));
        }

        var totalCount = await asyncExecuter.LongCountAsync(query, cancellationToken);

        // 默认按排序号再按名称；显式排序由列表页的列头传入。
        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "sort asc, name asc" : input.Sorting;
        var roles = await asyncExecuter.ToListAsync(
            query.OrderBy(sorting).Skip(input.Offset).Take(input.Limit),
            cancellationToken);

        var result = await MapToOutputsAsync(roles, cancellationToken);
        return new PagedResultDto<RoleOutputDto>(totalCount, result);
    }

    public async Task<IReadOnlyList<RoleBriefDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var query = await roleRepository.GetQueryableAsync(cancellationToken);
        var roles = await asyncExecuter.ToListAsync(
            query.OrderBy(r => r.Sort).ThenBy(r => r.Name),
            cancellationToken);

        return objectMapper.Map<List<Role>, List<RoleBriefDto>>(roles);
    }

    public async Task<RoleOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await GetRoleOrThrowAsync(id, cancellationToken);
        return await MapToOutputAsync(role, cancellationToken);
    }

    public async Task<RoleOutputDto> CreateAsync(
        CreateRoleInputDto input,
        CancellationToken cancellationToken = default)
    {
        var name = input.Name.Trim();

        if (await roleRepository.AnyAsync(r => r.Name == name, cancellationToken))
        {
            throw new BadRequestException($"Role '{name}' already exists.")
#if (IncludeLocalization)
                .WithLocalization("Role:NameAlreadyUsed")
                .WithData("Name", name)
#endif
                ;
        }

        var role = new Role(
            name,
            input.DisplayName.Trim(),
            input.Description?.Trim(),
            isStatic: false,
            isDefault: input.IsDefault,
            sort: input.Sort);

        await roleRepository.InsertAsync(role, cancellationToken);
        logger.LogInformation("创建角色成功 {Name} (ID: {Id})", role.Name, role.Id);

        return await MapToOutputAsync(role, cancellationToken);
    }

    public async Task<RoleOutputDto> UpdateAsync(
        Guid id,
        UpdateRoleInputDto input,
        CancellationToken cancellationToken = default)
    {
        var role = await GetRoleOrThrowAsync(id, cancellationToken);

        role.Update(input.DisplayName.Trim(), input.Description?.Trim(), input.Sort);

        if (input.IsDefault)
        {
            role.SetAsDefault();
        }
        else
        {
            role.UnsetDefault();
        }

        await roleRepository.UpdateAsync(role, cancellationToken);
        logger.LogInformation("更新角色成功 {Name} (ID: {Id})", role.Name, role.Id);

        return await MapToOutputAsync(role, cancellationToken);
    }

    /// <remarks>
    /// 幂等：角色已不存在时也继续按 provider key 清理授权并返回成功。
    /// 删角色与清授权是两次提交，第二步失败会留下孤儿授予行；若此时还对重试报 404，
    /// "重试即可收敛"就没有任何入口，孤儿只能永久留着。
    /// </remarks>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role == null)
        {
            await permissionGrantManager.RemoveProviderAsync(
                PermissionGrantProviderNames.Role,
                id.ToString(),
                cancellationToken);
            return;
        }

        if (role.IsStatic)
        {
            throw new BadRequestException($"Built-in role '{role.Name}' cannot be deleted.")
#if (IncludeLocalization)
                .WithLocalization("Role:StaticRoleCannotBeDeleted")
                .WithData("Name", role.Name)
#endif
                ;
        }

        var userCount = await userRoleRepository.CountAsync(ur => ur.RoleId == id, cancellationToken);
        if (userCount > 0)
        {
            throw new BadRequestException(
                    $"Role '{role.Name}' still has {userCount} assigned user(s). Reassign them before deleting.")
#if (IncludeLocalization)
                .WithLocalization("Role:RoleStillAssigned")
                .WithData("Name", role.Name)
                .WithData("UserCount", userCount.ToString())
#endif
                ;
        }

        // 先删角色，再清理授权——两者无法做成一个事务：授权管理器持有的是外层请求的
        // DbContext，而工作单元会另开一个 DI scope 和另一个 DbContext，罩上去也只是两次独立提交。
        // 为此让通用授权组件反过来依赖工作单元组件，代价远大于收益。
        //
        // 于是选一个无害的失败形态：第二步失败时留下的是"角色已删、授予行残留"，
        // 而角色 Id 是 Guid v7 永不重用，这些行无人可及；RemoveProviderAsync 幂等，重试或周期清理即可。
        // 反过来先清权限，失败时会留下"角色还在、权限已清空"——一个看着能用、实际什么都不能做的角色。
        await roleRepository.DeleteAsync(role, cancellationToken);

        // 角色被永久删除，授予与授权版本一并清理。
        // 不能用"替换为空集合"：那是撤销语义，会保留并递增版本（给"还有人在编辑"用），
        // 主体都没了还留着版本行只会变成永久孤儿。
        await permissionGrantManager.RemoveProviderAsync(
            PermissionGrantProviderNames.Role,
            id.ToString(),
            cancellationToken);

        logger.LogInformation("删除角色成功 {Name} (ID: {Id})", role.Name, role.Id);
    }

    private async Task<Role> GetRoleOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role == null)
        {
            throw new NotFoundException($"Role '{id}' was not found.")
#if (IncludeLocalization)
                .WithLocalization("Role:NotFound")
                .WithData("Id", id.ToString())
#endif
                ;
        }

        return role;
    }

    private async Task<RoleOutputDto> MapToOutputAsync(Role role, CancellationToken cancellationToken)
    {
        var context = await CreateMappingContextAsync([role], cancellationToken);
        return objectMapper.Map<Role, RoleOutputDto>(role, context);
    }

    private async Task<List<RoleOutputDto>> MapToOutputsAsync(
        List<Role> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        var context = await CreateMappingContextAsync(roles, cancellationToken);
        return objectMapper.Map<List<Role>, List<RoleOutputDto>>(roles, context);
    }

    /// <summary>
    /// 一次性取回本批角色的用户数与授予数：两者都走批量查询，往返次数与角色数量无关。
    /// </summary>
    private async Task<Dictionary<string, object>> CreateMappingContextAsync(
        IReadOnlyCollection<Role> roles,
        CancellationToken cancellationToken)
    {
        var roleIds = roles.Select(role => role.Id).ToList();

        var userRoleQuery = await userRoleRepository.GetQueryableAsync(cancellationToken);
        var userRoles = await asyncExecuter.ToListAsync(
            userRoleQuery.Where(ur => roleIds.Contains(ur.RoleId)),
            cancellationToken);

        var userCounts = userRoles
            .GroupBy(ur => ur.RoleId)
            .ToDictionary(group => group.Key, group => group.Count());

        var grantSets = await permissionGrantStore.GetGrantsAsync(
            PermissionGrantProviderNames.Role,
            [.. roleIds.Select(id => id.ToString())],
            cancellationToken);

        var permissionCounts = grantSets.ToDictionary(
            set => Guid.Parse(set.ProviderKey),
            set => set.PermissionNames.Count);

        return new Dictionary<string, object>
        {
            [RoleProfile.UserCountsKey] = (IReadOnlyDictionary<Guid, int>)userCounts,
            [RoleProfile.PermissionCountsKey] = (IReadOnlyDictionary<Guid, int>)permissionCounts
        };
    }
}
#endif
