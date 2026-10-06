using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Roles.Mappings;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Leistd.Authorization.Constants;
using Leistd.ExceptionHandling;
using Leistd.ObjectMapping;
using Leistd.UnitOfWork.Attributes;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.Data.Paging;
#if (IncludeRealTime)
using CompanyName.ProjectName.Application.RealTime;
using CompanyName.ProjectName.Application.Roles.Events;
using Leistd.EventBus.Abstractions;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Extensions;
#endif

namespace CompanyName.ProjectName.Application.Roles.AppServices;

/// <summary>
/// 角色应用服务
/// </summary>
public class RoleAppService(
    IRepository<Role, Guid> roleRepository,
    RoleDomainService roleDomainService,
    IRepository<UserRole, Guid> userRoleRepository,
    IRepository<User, Guid> userRepository,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
    IOperationRecorder operationRecorder,
    IObjectMapper objectMapper,
    ILogger<RoleAppService> logger,
#if (IncludeRealTime)
    ILocalEventBus localEventBus,
    ICurrentTenant currentTenant,
#endif
    IQueryableAsyncExecuter asyncExecuter) : BaseAppService, IRoleAppService
{
    public async Task<PagedResult<RoleOutputDto>> GetPagedListAsync(
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
        return new PagedResult<RoleOutputDto>(totalCount, result);
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

    public async Task<IReadOnlyList<RoleBriefOutputDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var query = await roleRepository.GetQueryableAsync(cancellationToken);
        var roles = await asyncExecuter.ToListAsync(
            query.OrderBy(r => r.Sort).ThenBy(r => r.Name),
            cancellationToken);

        return objectMapper.Map<List<Role>, List<RoleBriefOutputDto>>(roles);
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
        var role = await roleDomainService.CreateAsync(
            input.Name.Trim(),
            input.DisplayName.Trim(),
            input.Description?.Trim(),
            input.IsDefault,
            input.Sort,
            cancellationToken);

        // 目标名取显示名、退到名称：同类记录必须用同一套取值规则，
        // 一半存显示名一半存名称会让同一张表里的同类行长得不一样。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.RoleCreated,
            OperationTarget.For(role.Id, role.DisplayName ?? role.Name),
            PermissionConstant.Roles.Create,
            cancellationToken);
#if (IncludeRealTime)
        await PublishRoleListChangedAsync(cancellationToken);
#endif

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
#if (IncludeRealTime)
        await PublishRoleListChangedAsync(cancellationToken);
#endif

        return await MapToOutputAsync(role, cancellationToken);
    }

    /// <remarks>
    /// 删角色、清关联、清授权与成功记录在同一个工作单元里提交（授权存储与业务表同一个上下文）：
    /// 任何一步失败整体回滚，不会出现"接口报错但角色已删"的半成品。
    /// 角色已不存在时仍按 provider key 清理授权并返回成功，兜住升级前分两次提交留下的孤儿授予行。
    /// </remarks>
    [UnitOfWork]
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
            throw new BusinessException(RoleErrorCodes.StaticRoleCannotBeDeleted, $"Built-in role '{role.Name}' cannot be deleted.")
                .WithData("Name", role.Name);
        }

        // 只数还在的用户：删除用户是软删除，关联行随用户保留（恢复时一并回来），
        // 数进去的话角色就被一个界面上看不到、也无法改派的人永久卡住
        var userCount = await asyncExecuter.CountAsync(
            (await AssignmentsOfExistingUsersAsync(cancellationToken)).Where(ur => ur.RoleId == id),
            cancellationToken);
        if (userCount > 0)
        {
            throw new BusinessException(RoleErrorCodes.RoleStillAssigned,
                    $"Role '{role.Name}' still has {userCount} assigned user(s). Reassign them before deleting.")
                .WithData("Name", role.Name)
                .WithData("UserCount", userCount);
        }

        await roleRepository.DeleteAsync(role, cancellationToken);
        // 剩下的关联都属于已删除的用户，随角色一并删除，不留指向已删角色的孤儿行
        await userRoleRepository.DeleteManyAsync(
            await userRoleRepository.GetListAsync(ur => ur.RoleId == id, cancellationToken),
            cancellationToken);

        // 角色被永久删除，授予与授权版本一并清理。
        // 不能用"替换为空集合"：那是撤销语义，会保留并递增版本（给"还有人在编辑"用），
        // 主体都没了还留着版本行只会变成永久孤儿。
        await permissionGrantManager.RemoveProviderAsync(
            PermissionGrantProviderNames.Role,
            id.ToString(),
            cancellationToken);

        logger.LogInformation("Role deleted: {Name} (ID: {Id})", role.Name, role.Id);

        // 只记真正删掉了角色的这条路径：上面 role == null 那支是幂等清理，什么也没删，
        // 记下来会让审计里出现一堆"删除了一个本来就不存在的角色"。
        // 名字必须在删除前就握在手里（role 变量即是）：删完再查就什么都查不到了，
        // 而审计要回答的正是"当时删掉的是哪一个"。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.RoleDeleted,
            OperationTarget.For(id, role.DisplayName ?? role.Name),
            PermissionConstant.Roles.Delete,
            cancellationToken);
#if (IncludeRealTime)
        await PublishRoleListChangedAsync(cancellationToken);
#endif
    }
#if (IncludeRealTime)

    // 有工作单元时事件推迟到提交之后分发，回滚的写入不推送；新建、修改没有工作单元，仓储已保存后立即发布
    private Task PublishRoleListChangedAsync(CancellationToken cancellationToken) =>
        localEventBus.PublishAsync(
            new RoleListChangedEvent(currentTenant.ScopeKey(AppRealTimeResources.Roles)),
            cancellationToken);
#endif

    private async Task<Role> GetRoleOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role == null)
        {
            throw new BusinessException(RoleErrorCodes.NotFound, $"Role '{id}' was not found.")
                .WithData("Id", id);
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

        var userRoles = await asyncExecuter.ToListAsync(
            (await AssignmentsOfExistingUsersAsync(cancellationToken)).Where(ur => roleIds.Contains(ur.RoleId)),
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
            [RoleMappings.UserCountsKey] = (IReadOnlyDictionary<Guid, int>)userCounts,
            [RoleMappings.PermissionCountsKey] = (IReadOnlyDictionary<Guid, int>)permissionCounts
        };
    }

    /// <summary>未删除用户的角色关联：已删除用户的关联行保留着，但不算"已分配"。</summary>
    private async Task<IQueryable<UserRole>> AssignmentsOfExistingUsersAsync(CancellationToken cancellationToken)
    {
        var users = await userRepository.GetQueryableAsync(cancellationToken);
        var userRoles = await userRoleRepository.GetQueryableAsync(cancellationToken);
        return userRoles.Where(ur => users.Any(u => u.Id == ur.UserId));
    }
}
