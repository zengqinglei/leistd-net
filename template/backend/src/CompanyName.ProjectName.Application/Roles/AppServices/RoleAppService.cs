#if (IncludeRoles)
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

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await GetRoleOrThrowAsync(id, cancellationToken);

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

        // 角色被删除后其授予记录不再有主体，一并清理，避免留下无法被界面看到的孤儿授予。
        await permissionGrantManager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            id.ToString(),
            [],
            cancellationToken: cancellationToken);

        await roleRepository.DeleteAsync(role, cancellationToken);
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
    /// 一次性取回本批角色的用户数与授予数，避免逐行查询。
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

        var permissionCounts = new Dictionary<Guid, int>(roles.Count);
        foreach (var role in roles)
        {
            var grants = await permissionGrantStore.GetGrantsAsync(
                PermissionGrantProviderNames.Role,
                role.Id.ToString(),
                cancellationToken);
            permissionCounts[role.Id] = grants.Grants.Count;
        }

        return new Dictionary<string, object>
        {
            [RoleProfile.UserCountsKey] = (IReadOnlyDictionary<Guid, int>)userCounts,
            [RoleProfile.PermissionCountsKey] = (IReadOnlyDictionary<Guid, int>)permissionCounts
        };
    }
}
#endif
