using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Roles.Mappings;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Leistd.Authorization.Constants;
using Leistd.ExceptionHandling;
using Leistd.ObjectMapping;
using Leistd.Authorization.Abstractions;
using Leistd.ObjectMapping.Abstractions;

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

        var roles = await asyncExecuter.ToListAsync(
            ApplySorting(query, input.Sorting).Skip(input.Offset).Take(input.Limit),
            cancellationToken);

        var result = await MapToOutputsAsync(roles, cancellationToken);
        return new PagedResultDto<RoleOutputDto>(totalCount, result);
    }

    /// <summary>
    /// 角色列表的可排序字段
    /// </summary>
    /// <remarks>
    /// <para>不给"未传 sorting"单开一条分支：<see cref="SortingRequest.Parse"/> 已经把缺省字段
    /// 定为 <c>sort</c>，于是省略排序与显式 <c>sort asc</c> 走的是同一段代码、结果必然一致。
    /// 这两者必须一致——列表页即使 URL 上没有排序参数，也会把默认排序状态转成 <c>sort asc</c>
    /// 发出来，所以它们是同一个列表的两种调用方式；各写一遍就会各自漂移。</para>
    /// <para>排序号相同时按名称，与 <see cref="GetAllAsync"/> 同口径。名称始终升序：
    /// 它是给人看的次序，不是调用方选的排序键。末尾固定追加 <c>Id</c> 收口——排序键有并列值时，
    /// 缺少稳定次序会让同一行在翻页时重复出现或整行漏掉。</para>
    /// </remarks>
    private static IQueryable<Role> ApplySorting(IQueryable<Role> query, string? sorting)
    {
        var (field, descending) = SortingRequest.Parse(sorting, "sort");

        var ordered = field switch
        {
            "displayName" => SortingRequest.By(query, r => r.DisplayName, descending),
            "sort" => SortingRequest.By(query, r => r.Sort, descending).ThenBy(r => r.Name),
            "creationTime" => SortingRequest.By(query, r => r.CreationTime, descending),
            _ => throw SortingRequest.UnknownField(field)
        };

        return ordered.ThenBy(r => r.Id);
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
                .WithCode("Role:NameAlreadyUsed")
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
        logger.LogInformation("Role created: {Name} (ID: {Id})", role.Name, role.Id);

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
        logger.LogInformation("Role updated: {Name} (ID: {Id})", role.Name, role.Id);

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

        if (!role.CanBeDeleted())
        {
            throw new BadRequestException($"Built-in role '{role.Name}' cannot be deleted.")
#if (IncludeLocalization)
                .WithCode("Role:StaticRoleCannotBeDeleted")
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
                .WithCode("Role:RoleStillAssigned")
                .WithData("Name", role.Name)
                .WithData("UserCount", userCount.ToString())
#endif
                ;
        }

        // 角色删除与授权清理独立提交；先删角色，使清理失败时的残留授予不可达。
        // 角色 Id 不复用且 RemoveProviderAsync 幂等，因此可安全重试清理。
        await roleRepository.DeleteAsync(role, cancellationToken);

        // 角色被永久删除，授予与授权版本一并清理。
        // 不能用"替换为空集合"：那是撤销语义，会保留并递增版本（给"还有人在编辑"用），
        // 主体都没了还留着版本行只会变成永久孤儿。
        await permissionGrantManager.RemoveProviderAsync(
            PermissionGrantProviderNames.Role,
            id.ToString(),
            cancellationToken);

        logger.LogInformation("Role deleted: {Name} (ID: {Id})", role.Name, role.Id);
    }

    private async Task<Role> GetRoleOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role == null)
        {
            throw new NotFoundException($"Role '{id}' was not found.")
#if (IncludeLocalization)
                .WithCode("Role:NotFound")
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
