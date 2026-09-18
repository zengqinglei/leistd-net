using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Dtos;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.OperationRecords.Abstractions;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Exceptions;
using Leistd.Authorization.Permissions;
using Leistd.Authorization.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
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
    ,
    IOperationRecorder operationRecorder
    ,
    ICurrentTenant currentTenant
#if (IncludeLocalization)
    ,
    IStringLocalizerFactory localizerFactory
#endif
    ) : BaseAppService, IPermissionAppService
{
    /// <summary>
    /// 当前多租户侧别匹配：宿主侧权限对租户上下文不可见——
    /// 检查器已有同一硬边界，这里让 current 权限集与定义树同口径，
    /// 否则租户管理员会在菜单里看到点进去必然 403 的宿主功能。
    /// </summary>
    private bool MatchesCurrentSide(IPermissionDefinition definition)
        => definition.Side.HasFlag(currentTenant.IsAvailable ? MultiTenancySides.Tenant : MultiTenancySides.Host);

    private bool MatchesCurrentSide(string permissionName)
        => permissionDefinitionManager.GetOrNull(permissionName) is { } definition && MatchesCurrentSide(definition);

    public async Task<CurrentPermissionsOutputDto> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var subject = await permissionSubjectProvider.GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
        {
            throw new UnauthorizedException("The current request is not authenticated.")
#if (IncludeLocalization)
                .WithCode("Auth:Unauthenticated")
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
                VersionToken = "super-admin"
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
            VersionToken = grants.VersionToken
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

        // 框架异常自带状态码：并发冲突 409、未定义权限 400（"权限是否已定义且启用"由授予管理器
        // 统一把关，它是写入方、覆盖全部调用路径，此处不重复判断）。因此这里**没有**必须写的
        // try/catch——只在启用本地化时捕获一次，为的是挂上展示文案键。
        // 用 bare throw 而不是新建异常包一层：后者会丢掉原始异常类型，调用方就无法再按类型区分。
#if (IncludeLocalization)
        try
        {
            await permissionGrantManager.ReplaceGrantsAsync(
                providerName,
                providerKey,
                input.PermissionNames,
                input.ExpectedVersion,
                cancellationToken);
        }
        catch (PermissionGrantConcurrencyException exception)
        {
            exception.WithCode("Permission:ConcurrencyConflict");
            throw;
        }
        catch (UndefinedPermissionException exception)
        {
            exception
                .WithCode("Permission:UndefinedPermission")
                .WithData("Name", string.Join(", ", exception.PermissionNames));
            throw;
        }
#else
        await permissionGrantManager.ReplaceGrantsAsync(
            providerName,
            providerKey,
            input.PermissionNames,
            input.ExpectedVersion,
            cancellationToken);
#endif

        // 权限授予变更是整套权限体系里最敏感的写操作之一——它直接改变"这个主体能做什么"。
        // 目标标识用 $"{providerName}/{providerKey}"，与 PermissionController 上注解拼出的
        // "Role/{roleId}" 逐字一致；两边写法一旦分叉，按目标检索就只能查到一半，且不会报错。
        // 本方法没有 [UnitOfWork]：授予管理器自己管事务，这条记录写入即时生效。
        //
        // 授权依据按 providerName 判定，**不能写死成角色那个权限**：本方法是通用的
        // （providerName 取值域见 PermissionGrantProviderNames：User 与 Role 两种），
        // 写死会让"给用户直授权限"这条路径在审计表里留下一条撒谎的"凭什么"——
        // 而这种错编译器抓不到，只会在事后追责时把人指错方向。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.PermissionGrantsReplaced,
            OperationTarget.For(
                $"{providerName}/{providerKey}",
                await ResolveSubjectNameOrNullAsync(providerName, providerKey, cancellationToken)),
            providerName == PermissionGrantProviderNames.User
                ? OperationRecordAuthorizations.DirectUserGrant
                : PermissionConstant.Roles.ManagePermissions,
            cancellationToken);

        return await GetGrantsAsync(providerName, providerKey, cancellationToken);
    }

    /// <summary>
    /// 取主体的显示名，供审计记录的目标名快照使用；取不到时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为一个审计字段额外查一次库，是值得的。</b>权限授予替换是 <c>Critical</c> 级动作
    /// ——"改一个角色能做什么"——而目标若显示成裸 GUID，恰恰让这条最该被看懂的记录最难看懂。</para>
    /// <para><b>但绝不能让它把一次已成功的授权变更变成 500。</b>授予此刻已经写入，
    /// 查名只是为了让记录好看；查不到就退化为无名，记录照常落库。
    /// 取消照常向上传播——那不是故障，是调用方主动中止。</para>
    /// <para>名字取值规则与其他调用点一致：显示名优先，退到名称/登录名。</para>
    /// </remarks>
    private async Task<string?> ResolveSubjectNameOrNullAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(providerKey, out var id))
        {
            return null;
        }

        try
        {
            // 用 GetByIdAsync 而不是按谓词查：这里就是按主键取实体。
            // （IRepository 提供的是 GetOneAsync / GetFirstAsync / GetByIdAsync / AnyAsync / GetListAsync，
            //  没有 FirstOrDefaultAsync——那是 EF 对 IQueryable 的扩展方法，不在仓储契约上。）
            return providerName switch
            {
                PermissionGrantProviderNames.User =>
                    await userRepository.GetByIdAsync(id, cancellationToken)
                        is { } user ? user.DisplayName ?? user.Username : null,
                PermissionGrantProviderNames.Role =>
                    await roleRepository.GetByIdAsync(id, cancellationToken)
                        is { } role ? role.DisplayName ?? role.Name : null,
                _ => null
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 把权限定义里的显示名按当前请求的 culture 翻译。
    /// </summary>
    /// <remarks>
    /// 定义本身是 Singleton、启动时加载，存的是本地化键；翻译必须发生在响应阶段，
    /// 否则先到的那个请求的语言会被固化给所有人。翻译不可得时回退到权限名而不是键——
    /// 键带 <c>Permission:</c> 这样的内部前缀，直接摆到界面上是给用户看内部标识。
    /// </remarks>
    private string Localize(string? displayNameKey, string fallback)
    {
#if (IncludeLocalization)
        if (!string.IsNullOrWhiteSpace(displayNameKey))
        {
            var localizer = localizerFactory.Create(typeof(PermissionAppService));
            var localized = localizer[displayNameKey];
            if (!localized.ResourceNotFound)
                return localized.Value;
        }
#endif
        return fallback;
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
            Version = direct.Version,
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
                .WithCode("Permission:InvalidProviderKey")
#endif
                ;
        }

        var exists = providerName switch
        {
            PermissionGrantProviderNames.User => await userRepository.AnyAsync(u => u.Id == id, cancellationToken),
            PermissionGrantProviderNames.Role => await roleRepository.AnyAsync(r => r.Id == id, cancellationToken),
            _ => throw new BadRequestException($"Unsupported provider '{providerName}'.")
#if (IncludeLocalization)
                .WithCode("Permission:UnsupportedProvider")
                .WithData("Provider", providerName)
#endif
        };

        if (!exists)
        {
            throw new NotFoundException($"Subject '{providerName}/{providerKey}' was not found.")
#if (IncludeLocalization)
                .WithCode("Permission:SubjectNotFound")
#endif
                ;
        }

        return id;
    }
}
