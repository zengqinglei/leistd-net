#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
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
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository
#if (IncludeLocalization)
    ,
    IStringLocalizerFactory localizerFactory
#endif
    ) : BaseAppService, IPermissionAppService
{
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

        var effects = grants.GetEffectiveEffects();
        var permissions = effects
            .Where(x => x.Value == PermissionGrantEffect.Granted
                        && permissionDefinitionManager.IsEffectivelyEnabled(x.Key))
            .Select(x => x.Key)
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
                    .Select(ToTree)
                    .ToList()
            })
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
        var inherited = await GetInheritedEffectsAsync(providerName, providerKey, cancellationToken);

        return BuildOutput(providerName, providerKey, direct, inherited);
    }

    public async Task<PermissionGrantsOutputDto> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubjectExistsAsync(providerName, providerKey, cancellationToken);

        var grants = new List<PermissionGrant>(input.Grants.Count);
        foreach (var grant in input.Grants)
        {
            if (!permissionDefinitionManager.IsEffectivelyEnabled(grant.Name))
            {
                throw new BadRequestException($"Permission '{grant.Name}' is not defined or is disabled.")
#if (IncludeLocalization)
                    .WithLocalization("Permission:UndefinedPermission")
                    .WithData("Name", grant.Name)
#endif
                    ;
            }

            grants.Add(new PermissionGrant(
                grant.Name,
                grant.Effect == nameof(PermissionGrantEffect.Prohibited)
                    ? PermissionGrantEffect.Prohibited
                    : PermissionGrantEffect.Granted));
        }

        try
        {
            await permissionGrantManager.ReplaceGrantsAsync(
                providerName,
                providerKey,
                grants,
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

    private PermissionDefinitionOutputDto ToTree(IPermissionDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = Localize(definition.DisplayName, definition.Name),
            ParentName = definition.Parent?.Name,
            Children = definition.Children.Select(ToTree).ToList()
        };

    /// <summary>
    /// 用户主体的继承来源是其所属角色；角色主体没有上游来源。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, PermissionGrantEffect>> GetInheritedEffectsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        if (providerName != PermissionGrantProviderNames.User || !Guid.TryParse(providerKey, out var userId))
            return new Dictionary<string, PermissionGrantEffect>(StringComparer.Ordinal);

        var roleIds = (await userRoleRepository.GetListAsync(ur => ur.UserId == userId, cancellationToken))
            .Select(ur => ur.RoleId.ToString())
            .ToArray();

        if (roleIds.Length == 0)
            return new Dictionary<string, PermissionGrantEffect>(StringComparer.Ordinal);

        // 只取角色部分：传入空用户 Key，避免把用户直授混进"继承"列。
        var roleGrants = await permissionGrantStore.GetGrantsForSubjectAsync(
            string.Empty,
            roleIds,
            cancellationToken);

        return roleGrants.GetEffectiveEffects();
    }

    private PermissionGrantsOutputDto BuildOutput(
        string providerName,
        string providerKey,
        PermissionGrantSet direct,
        IReadOnlyDictionary<string, PermissionGrantEffect> inherited)
    {
        var directEffects = direct.Grants
            .ToDictionary(x => x.PermissionName, x => x.Effect, StringComparer.Ordinal);

        var states = permissionDefinitionManager
            .GetAll()
            .Where(definition => permissionDefinitionManager.IsEffectivelyEnabled(definition.Name))
            .Select(definition =>
            {
                directEffects.TryGetValue(definition.Name, out var directEffect);
                inherited.TryGetValue(definition.Name, out var inheritedEffect);

                var hasDirect = directEffects.ContainsKey(definition.Name);
                var hasInherited = inherited.ContainsKey(definition.Name);

                var effective = !(hasDirect && directEffect == PermissionGrantEffect.Prohibited)
                                && !(hasInherited && inheritedEffect == PermissionGrantEffect.Prohibited)
                                && ((hasDirect && directEffect == PermissionGrantEffect.Granted)
                                    || (hasInherited && inheritedEffect == PermissionGrantEffect.Granted));

                return new PermissionGrantStateDto
                {
                    Name = definition.Name,
                    Direct = hasDirect ? directEffect.ToString() : null,
                    Inherited = hasInherited ? inheritedEffect.ToString() : null,
                    Effective = effective
                };
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

    private async Task EnsureSubjectExistsAsync(
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
    }
}
#endif
