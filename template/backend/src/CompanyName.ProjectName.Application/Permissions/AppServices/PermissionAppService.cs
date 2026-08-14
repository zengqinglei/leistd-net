#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
#if (TenancyEnabled)
using Leistd.MultiTenancy;
#endif
#if (IncludeLocalization)
using Microsoft.Extensions.Localization;
#endif

namespace CompanyName.ProjectName.Application.Permissions.AppServices;

/// <summary>
/// 权限管理应用服务
/// </summary>
/// <remarks>
/// 权限树完全由 <see cref="IPermissionDefinitionManager"/> 的定义生成，前端不硬编码任何权限列表；
/// 授予的读写都以主体为单位整体进行，保存是单次请求、单个事务。
/// </remarks>
public class PermissionAppService(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionSubjectProvider permissionSubjectProvider,
    IPermissionGrantStore permissionGrantStore,
    IPermissionGrantManager permissionGrantManager,
    IRepository<User, Guid> userRepository,
    IRepository<Role, Guid> roleRepository
#if (TenancyEnabled)
    ,
    ICurrentTenant currentTenant
#endif
#if (IncludeLocalization)
    ,
    IStringLocalizerFactory localizerFactory
#endif
    ) : BaseAppService, IPermissionAppService
{
#if (TenancyEnabled)
    /// <summary>
    /// 当前多租户侧别匹配：宿主侧权限（如 App.Tenants.*）对租户上下文不可见——
    /// 检查器已有同一硬边界，这里让 current 权限集与定义树同口径，
    /// 否则租户超管会在菜单里看到点进去必然 403 的宿主功能。
    /// </summary>
    private bool MatchesCurrentSide(IPermissionDefinition definition)
        => definition.Side.HasFlag(currentTenant.IsAvailable ? MultiTenancySides.Tenant : MultiTenancySides.Host);

    private bool MatchesCurrentSide(string permissionName)
        => permissionDefinitionManager.GetOrNull(permissionName) is { } definition && MatchesCurrentSide(definition);
#else
    private static bool MatchesCurrentSide(IPermissionDefinition definition) => true;

    private static bool MatchesCurrentSide(string permissionName) => true;
#endif

    public async Task<CurrentPermissionsOutputDto> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var subject = await permissionSubjectProvider.GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
        {
            throw new UnauthorizedException("The current request is not authenticated.")
#if (IncludeLocalization)
                .WithLocalization("Auth:Unauthenticated")
#endif
                ;
        }

        if (subject.IsSuperAdmin)
        {
            // 超级管理员旁路功能权限：直接下发全部已启用的定义，前端无需再做特殊分支。
            var all = permissionDefinitionManager
                .GetAll()
                .Where(x => permissionDefinitionManager.IsEffectivelyEnabled(x.Name))
                .Where(MatchesCurrentSide)
                .Select(x => x.Name)
                .ToList();

            return new CurrentPermissionsOutputDto
            {
                Permissions = all,
                IsSuperAdmin = true,
                Revision = "super-admin"
            };
        }

        var grants = await permissionGrantStore.GetGrantsForSubjectAsync(
            subject.UserId,
            subject.RoleIds,
            cancellationToken);

        var permissions = grants
            .GetGrantedNames()
            .Where(permissionDefinitionManager.IsEffectivelyEnabled)
            .Where(MatchesCurrentSide)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new CurrentPermissionsOutputDto
        {
            Permissions = permissions,
            IsSuperAdmin = false,
            Revision = grants.Revision
        };
    }

    public Task<IReadOnlyList<PermissionDefinitionGroupOutputDto>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PermissionDefinitionGroupOutputDto> groups = permissionDefinitionManager
            .GetGroups()
            .Select(group => new PermissionDefinitionGroupOutputDto
            {
                Name = group.Name,
                DisplayName = Localize(group.DisplayName, group.Name),
                Permissions = group.Permissions
                    .Where(permission => permissionDefinitionManager.IsEffectivelyEnabled(permission.Name))
                    .Where(MatchesCurrentSide)
                    .Select(ToTree)
                    .ToList()
            })
            // 整组都被禁用时不下发空壳：界面上一个只有标题、点开什么都没有的分组，
            // 除了让人怀疑数据没加载出来之外没有任何作用。
            .Where(group => group.Permissions.Count > 0)
            .ToList();

        return Task.FromResult(groups);
    }

    public async Task<PermissionGrantsOutputDto> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubjectExistsAsync(providerName, providerKey, cancellationToken);

        var direct = await permissionGrantStore.GetGrantsAsync(providerName, providerKey, cancellationToken);

        return BuildOutput(providerName, providerKey, direct);
    }

    public async Task<PermissionGrantsOutputDto> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubjectExistsAsync(providerName, providerKey, cancellationToken);

        try
        {
            await permissionGrantManager.ReplaceGrantsAsync(
                providerName,
                providerKey,
                input.PermissionNames,
                input.ExpectedRevision,
                cancellationToken);
        }
        catch (PermissionGrantConcurrencyException exception)
        {
            throw new ConflictException(
                    "The permissions were changed by someone else. Reload and try again.",
                    exception)
#if (IncludeLocalization)
                .WithLocalization("Permission:ConcurrencyConflict")
#endif
                ;
        }
        // "权限是否已定义且启用"由授予管理器统一把关（它是写入方，覆盖全部调用路径），
        // 这里只负责把领域异常翻译成对外契约与展示文案，不重复这条判断。
        catch (UndefinedPermissionException exception)
        {
            throw new BadRequestException(exception.Message, exception)
#if (IncludeLocalization)
                .WithLocalization("Permission:UndefinedPermission")
                .WithData("Name", string.Join(", ", exception.PermissionNames))
#endif
                ;
        }

        return await GetGrantsAsync(providerName, providerKey, cancellationToken);
    }

    /// <summary>
    /// 把权限定义里的显示名按当前请求的 culture 翻译。
    /// </summary>
    /// <remarks>
    /// 定义本身是 Singleton、启动时加载，存的是本地化键；翻译必须发生在响应阶段，
    /// 否则先到的那个请求的语言会被固化给所有人。词条缺失时回退到键，再回退到权限名，
    /// 保证界面永远不会出现空白节点。
    /// </remarks>
    private string Localize(string? displayNameKey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayNameKey))
            return fallback;

#if (IncludeLocalization)
        var localizer = localizerFactory.Create(typeof(PermissionAppService));
        var localized = localizer[displayNameKey];
        return localized.ResourceNotFound ? displayNameKey : localized.Value;
#else
        return displayNameKey;
#endif
    }

    /// <remarks>
    /// 子节点同样要过滤：启用的父级下面挂着一个被禁用的子权限时，若照单下发，
    /// 界面会把它渲染成可勾选项，而 Manager 写入时又会以"未定义或已禁用"拒绝，
    /// 用户只能得到一个无从解释的 400。能不能勾，必须与能不能存保持同一判据。
    /// </remarks>
    private PermissionDefinitionOutputDto ToTree(IPermissionDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = Localize(definition.DisplayName, definition.Name),
            ParentName = definition.Parent?.Name,
            Children = definition.Children
                .Where(child => permissionDefinitionManager.IsEffectivelyEnabled(child.Name))
                .Where(MatchesCurrentSide)
                .Select(ToTree)
                .ToList()
        };

    private PermissionGrantsOutputDto BuildOutput(
        string providerName,
        string providerKey,
        PermissionGrantSet direct)
    {
        var granted = direct.PermissionNames.ToHashSet(StringComparer.Ordinal);

        var states = permissionDefinitionManager
            .GetAll()
            .Where(definition => permissionDefinitionManager.IsEffectivelyEnabled(definition.Name))
            .Where(MatchesCurrentSide)
            .Select(definition => new PermissionGrantStateDto
            {
                Name = definition.Name,
                Granted = granted.Contains(definition.Name)
            })
            .ToList();

        return new PermissionGrantsOutputDto
        {
            ProviderName = providerName,
            ProviderKey = providerKey,
            Revision = direct.Revision,
            Grants = states
        };
    }

    /// <summary>
    /// 校验主体 Key 合法且主体存在，并把解析结果交给调用方，避免下游重复解析。
    /// </summary>
    /// <remarks>
    /// providerName / providerKey 来自路由段而非入口 DTO，注解覆盖不到，因此校验落在这里；
    /// 这是该请求上这两项的唯一校验点。
    /// </remarks>
    private async Task<Guid> EnsureSubjectExistsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(providerKey, out var id))
        {
            throw new BadRequestException($"'{providerKey}' is not a valid subject key.")
#if (IncludeLocalization)
                .WithLocalization("Permission:InvalidProviderKey")
#endif
                ;
        }

        var exists = providerName switch
        {
            PermissionGrantProviderNames.User => await userRepository.AnyAsync(u => u.Id == id, cancellationToken),
            PermissionGrantProviderNames.Role => await roleRepository.AnyAsync(r => r.Id == id, cancellationToken),
            _ => throw new BadRequestException($"Unsupported provider '{providerName}'.")
#if (IncludeLocalization)
                .WithLocalization("Permission:UnsupportedProvider")
                .WithData("Provider", providerName)
#endif
        };

        if (!exists)
        {
            throw new NotFoundException($"Subject '{providerName}/{providerKey}' was not found.")
#if (IncludeLocalization)
                .WithLocalization("Permission:SubjectNotFound")
#endif
                ;
        }

        return id;
    }
}
#endif
