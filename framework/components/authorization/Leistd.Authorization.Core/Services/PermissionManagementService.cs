using Leistd.Authorization.Abstractions;
using Leistd.Authorization.Dtos;
using Leistd.Authorization.Events;
using Leistd.Authorization.Options;
using Leistd.Authorization.Permissions;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Authorization.Services;

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
    private MultiTenancySides CurrentSide =>
        currentTenant?.IsAvailable == true ? MultiTenancySides.Tenant : MultiTenancySides.Host;

    public async Task<CurrentPermissionsOutputDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken)
            ?? throw new UnauthorizedException("The current identity is not a permission subject.")
                .WithCode(PermissionErrorCodes.SubjectUnavailable);

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
                DisplayName = Localize(localizer, group.DisplayName, group.Name),
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
            throw new UnprocessableEntityException(
                "permissionNames",
                $"At most {ReplacePermissionGrantsInputDto.MaximumPermissionCount} permissions can be replaced at once.");
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
           ?? throw new NotFoundException($"Subject '{providerName}/{providerKey}' was not found.")
               .WithCode(PermissionErrorCodes.SubjectNotFound)
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
            DisplayName = Localize(localizer, definition.DisplayName, definition.Name),
            ParentName = definition.Parent?.Name,
            Children = [.. definition.Children
                .Where(child => definitionManager.IsAvailableOn(child.Name, side))
                .Select(child => ToTree(child, side, localizer))]
        };

    private IStringLocalizer? Localizer()
        => options.Value.LocalizationResource is { } resource ? localizerFactory?.Create(resource) : null;

    // 定义是 Singleton、启动时加载，翻译必须发生在响应阶段，否则先到的那个请求的语言会被固化给所有人
    private static string Localize(IStringLocalizer? localizer, string? key, string fallback)
    {
        if (localizer is null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var localized = localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}
