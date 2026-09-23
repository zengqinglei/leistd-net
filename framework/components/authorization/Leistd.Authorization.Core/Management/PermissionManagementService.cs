using System.ComponentModel.DataAnnotations;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.Authorization.Dtos;
using Leistd.Authorization.Events;
using Leistd.Authorization.Options;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Authorization.Management;

internal sealed class PermissionManagementService(
    IPermissionDefinitionManager definitionManager,
    IPermissionSubjectProvider subjectProvider,
    IPermissionGrantStore grantStore,
    IPermissionGrantManager grantManager,
    IPermissionSubjectDirectory subjectDirectory,
    IOptions<PermissionManagementOptions> options,
    ICurrentTenant? currentTenant = null,
    ILocalEventBus? eventBus = null,
    IStringLocalizerFactory? localizerFactory = null) : IPermissionManagementService
{
    // 当前身份不是权限主体时的版本标记
    private const string NoSubjectVersionToken = "no-subject";

    private MultiTenancySides CurrentSide =>
        currentTenant?.IsAvailable == true ? MultiTenancySides.Tenant : MultiTenancySides.Host;

    public async Task<CurrentPermissionsOutputDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);

        // 问"我有哪些权限"而当前身份不在本权限主体空间里时，正确答案是"一个都没有"，不是"你没登录"。
        //
        // 端点挂着 RequireAuthorization，能走到这里的调用方**必然已认证**，回 401 是在说假话；
        // 客户端据此去重新登录，登录成功后再问一次、再拿到 401，就是死循环。双 realm 部署
        // （员工走 RBAC、客户走另一套身份）会稳定踩中：客户令牌按设计就不落在员工的主体空间里。
        //
        // 这不是放松校验：空集合意味着任何权限判定都不通过，与抛异常的拒绝效果一致。
        // 需要区分"没有主体"与"有主体但没授权"的调用方看 IsSuperAdmin 之外的业务标识，
        // 不要把状态码当作那个信号。
        if (subject is null)
        {
            return new CurrentPermissionsOutputDto
            {
                Permissions = [],
                IsSuperAdmin = false,
                // 没有主体就没有可失效的授权，版本恒定；与真实主体的版本不会撞上
                VersionToken = NoSubjectVersionToken
            };
        }

        var side = CurrentSide;
        if (subject.IsSuperAdmin)
        {
            // 超级管理员旁路功能权限：直接下发全部可用定义，前端不必另做分支
            return new CurrentPermissionsOutputDto
            {
                Permissions = [.. definitionManager.GetAll().Select(d => d.Name).Where(name => definitionManager.IsAvailableOn(name, side))],
                IsSuperAdmin = true,
                VersionToken = "super-admin"
            };
        }

        var grants = await grantStore.GetGrantsForSubjectAsync(subject.UserId, subject.RoleIds, cancellationToken);
        return new CurrentPermissionsOutputDto
        {
            Permissions = [.. grants.GetGrantedNames()
                .Where(name => definitionManager.IsAvailableOn(name, side))
                .Order(StringComparer.Ordinal)],
            IsSuperAdmin = false,
            VersionToken = grants.VersionToken
        };
    }

    public Task<IReadOnlyList<PermissionDefinitionGroupOutputDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var side = CurrentSide;
        var localizer = Localizer();

        IReadOnlyList<PermissionDefinitionGroupOutputDto> groups = [.. definitionManager.GetGroups()
            .Select(group => new PermissionDefinitionGroupOutputDto
            {
                Name = group.Name,
                DisplayName = DisplayName(localizer, GroupKeyPrefix + group.Name, group.DisplayName, group.Name),
                Permissions = [.. group.Permissions
                    .Where(permission => definitionManager.IsAvailableOn(permission.Name, side))
                    .Select(permission => ToTree(permission, side, localizer))]
            })
            // 整组都不可用时不下发空壳：一个只有标题、点开什么都没有的分组只会让人怀疑数据没加载出来
            .Where(group => group.Permissions.Count > 0)];

        return Task.FromResult(groups);
    }

    public async Task<PermissionGrantsOutputDto> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        _ = await FindSubjectAsync(providerName, providerKey, cancellationToken);
        return BuildOutput(await grantStore.GetGrantsAsync(providerName, providerKey, cancellationToken));
    }

    public async Task<PermissionGrantsOutputDto> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        ReplacePermissionGrantsInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PermissionNames.Count > ReplacePermissionGrantsInputDto.MaximumPermissionCount)
        {
            throw new ValidationException(
                new ValidationResult(
                    $"At most {ReplacePermissionGrantsInputDto.MaximumPermissionCount} permissions can be replaced at once.",
                    [nameof(input.PermissionNames)]),
                validatingAttribute: null,
                value: input.PermissionNames);
        }

        var subject = await FindSubjectAsync(providerName, providerKey, cancellationToken);

        // 未定义或已停用的权限由授予管理器统一拒绝（它是写入方，覆盖全部调用路径），这里不重复判断
        var version = await grantManager.ReplaceGrantsAsync(
            providerName,
            providerKey,
            input.PermissionNames,
            input.ExpectedVersion,
            cancellationToken);

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(
                new PermissionGrantsReplacedEvent(providerName, providerKey, subject.DisplayName, version),
                cancellationToken);
        }

        return BuildOutput(await grantStore.GetGrantsAsync(providerName, providerKey, cancellationToken));
    }

    private async Task<PermissionSubjectInfo> FindSubjectAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
        => await subjectDirectory.FindAsync(providerName, providerKey, cancellationToken)
           ?? throw new BusinessException(PermissionErrorCodes.SubjectNotFound, $"Subject '{providerName}/{providerKey}' was not found.")
               .WithData("Provider", providerName)
               .WithData("Key", providerKey);

    private PermissionGrantsOutputDto BuildOutput(PermissionGrantSet direct)
    {
        var side = CurrentSide;
        var granted = direct.PermissionNames.ToHashSet(StringComparer.Ordinal);

        return new PermissionGrantsOutputDto
        {
            ProviderName = direct.ProviderName,
            ProviderKey = direct.ProviderKey,
            Version = direct.Version,
            Grants = [.. definitionManager.GetAll()
                .Where(definition => definitionManager.IsAvailableOn(definition.Name, side))
                .Select(definition => new PermissionGrantStateDto { Name = definition.Name, Granted = granted.Contains(definition.Name) })]
        };
    }

    // 子节点同样过滤：可用的父级下挂着停用的子权限时，界面会把它渲染成可勾选项，
    // 保存时却被授予管理器拒绝——能不能勾必须与能不能存同一判据
    private PermissionDefinitionOutputDto ToTree(IPermissionDefinition definition, MultiTenancySides side, IStringLocalizer? localizer)
        => new()
        {
            Name = definition.Name,
            DisplayName = DisplayName(localizer, PermissionKeyPrefix + definition.Name, definition.DisplayName, definition.Name),
            ParentName = definition.Parent?.Name,
            Children = [.. definition.Children
                .Where(child => definitionManager.IsAvailableOn(child.Name, side))
                .Select(child => ToTree(child, side, localizer))]
        };

    private IStringLocalizer? Localizer()
        => options.Value.LocalizationResource is { } resource ? localizerFactory?.Create(resource) : null;

    // 词条键按约定由名称拼出，与设置组件的 Setting:{名称} / SettingGroup:{分组} 同一模式：
    // 定义里的 DisplayName 是默认文案（未启用本地化或缺词条时直接显示），不再兼作词条键——
    // 否则不含本地化的宿主只能看到 App.Users.Create 这类技术名。
    internal const string PermissionKeyPrefix = "Permission:";
    internal const string GroupKeyPrefix = "PermissionGroup:";

    // 定义是 Singleton、启动时加载，翻译必须发生在响应阶段，否则先到的那个请求的语言会被固化给所有人
    private static string DisplayName(IStringLocalizer? localizer, string key, string? defaultText, string name)
    {
        if (localizer is not null)
        {
            var localized = localizer[key];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        return string.IsNullOrWhiteSpace(defaultText) ? name : defaultText;
    }
}
