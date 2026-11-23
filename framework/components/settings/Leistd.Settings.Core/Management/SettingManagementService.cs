using Leistd.ExceptionHandling;
using Leistd.Security.Users;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Stores;
using Leistd.Settings.Dtos;
using Leistd.Settings.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Management;

// 按层取原始覆盖值而不是 ISettingProvider 的回落结果，设置页才能分层编辑
internal sealed class SettingManagementService(
    ISettingStore settingStore,
    ISettingDefinitionManager definitionManager,
    ISettingManager settingManager,
    ICurrentUser currentUser,
    IOptions<SettingManagementOptions> options,
    IStringLocalizerFactory? localizerFactory = null,
    IEnumerable<SettingCheckRegistration>? checks = null,
    ISettingCheckStore? checkStore = null) : ISettingManagementService
{
    public async Task<IReadOnlyList<SettingOutputDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await LoadAsync(SettingScopes.Tenant, userId: null, cancellationToken);

        var isHost = settingStore.CanAccessHostScope;
        var definitions = definitionManager.GetAll()
            .Where(definition => definition.IsVisibleToClients)
            .Where(definition => isHost || !definition.Scopes.HasFlag(SettingScopes.Host))
            .ToList();

        var host = isHost && definitions.Any(definition => definition.Scopes.HasFlag(SettingScopes.Host))
            ? await LoadAsync(SettingScopes.Host, userId: null, cancellationToken)
            : Layer.Empty;

        var userId = currentUser.Id?.ToString();
        var user = userId is null
            ? Layer.Empty
            : await LoadAsync(SettingScopes.User, userId, cancellationToken);

        // 同一设置只认最后一次登记
        var checkable = (checks ?? [])
            .GroupBy(check => check.SettingName, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var results = checkable.Count > 0 && checkStore is not null
            ? await checkStore.GetAllAsync(cancellationToken)
            : new Dictionary<string, SettingCheckResult>();

        var localizer = SettingDisplayNames.CreateLocalizer(localizerFactory, options.Value);
        return [.. definitions.Select(definition => ToOutput(definition, user, tenant, host, localizer) with
        {
            // 测试作用于设置页编辑的那一层：只有租户层或宿主层可写的设置才有
            IsCheckable = checkable.Contains(definition.Name) && Manageable(definition),
            LastCheck = checkable.Contains(definition.Name) && Manageable(definition)
                && results.TryGetValue(definition.Name, out var last)
                ? SettingCheckResultDto.From(last)
                : null
        })];
    }

    private async Task<Layer> LoadAsync(SettingScopes scope, string? userId, CancellationToken cancellationToken)
        => new(
            await settingStore.GetAllAsync(scope, userId, cancellationToken),
            await settingStore.GetModificationsAsync(scope, userId, cancellationToken));

    // 一层的值与修改信息；修改信息只含有值、且在修改信息上线后写过的项
    private sealed record Layer(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyDictionary<string, SettingModification> Modifications)
    {
        public static Layer Empty { get; } = new(
            new Dictionary<string, string>(), new Dictionary<string, SettingModification>());
    }

    public async Task SetForCurrentUserAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var userId = currentUser.Id?.ToString()
            ?? throw new BusinessException(SettingErrorCodes.IdentityCannotOperate, "Only a user can change personal settings.");

        _ = EnsureVisibleToClients(input.Name);

        await settingManager.SetAsync(input.Name, input.Value, SettingScopes.User, userId, cancellationToken);
    }

    public async Task SetForCurrentTenantAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var definition = EnsureVisibleToClients(input.Name);

        // 进程级设置只有宿主那一行，租户上下文下由写入入口拒绝
        var scope = definition.Scopes.HasFlag(SettingScopes.Host) ? SettingScopes.Host : SettingScopes.Tenant;
        if (scope == SettingScopes.Host && !settingStore.CanAccessHostScope)
        {
            var displayName = SettingDisplayNames.Resolve(
                definition, SettingDisplayNames.CreateLocalizer(localizerFactory, options.Value));
            throw new BusinessException(SettingErrorCodes.HostOnly, $"Setting '{displayName}' is process-wide and can only be changed on the host.")
                .WithData("Name", displayName);
        }

        await settingManager.SetAsync(input.Name, input.Value, scope, cancellationToken: cancellationToken);
    }

    private SettingOutputDto ToOutput(
        ISettingDefinition definition,
        Layer user,
        Layer tenant,
        Layer host,
        IStringLocalizer? localizer)
    {
        var userLayer = definition.Scopes.HasFlag(SettingScopes.User) ? user : Layer.Empty;
        // 进程级设置只有宿主那一行，与租户层值一起放在 TenantValue
        var tenantLayer = definition.Scopes.HasFlag(SettingScopes.Host)
            ? host
            : definition.Scopes.HasFlag(SettingScopes.Tenant) ? tenant : Layer.Empty;
        var userValue = userLayer.Values.GetValueOrDefault(definition.Name);
        var tenantValue = tenantLayer.Values.GetValueOrDefault(definition.Name);

        var group = string.IsNullOrWhiteSpace(definition.Group) ? options.Value.DefaultGroup : definition.Group;
        var secret = definition.IsEncrypted;

        return new SettingOutputDto(
            definition.Name,
            SettingDisplayNames.Resolve(definition, localizer),
            group,
            SettingDisplayNames.Localize(localizer, $"SettingGroup:{group}") ?? group,
            secret ? null : userValue,
            secret ? null : tenantValue,
            secret ? null : definition.DefaultValue,
            definition.Scopes.HasFlag(SettingScopes.Tenant),
            definition.Scopes.HasFlag(SettingScopes.User),
            definition.Scopes.HasFlag(SettingScopes.Host),
            definition.ValueType == SettingValueType.Integer ? definition.Minimum : null,
            definition.ValueType == SettingValueType.Integer ? definition.Maximum : null,
            definition.ValueType == SettingValueType.Boolean,
            secret,
            secret && (tenantValue ?? userValue) is not null,
            definition.AllowedValues,
            UserModification: ToDto(userLayer.Modifications.GetValueOrDefault(definition.Name)),
            TenantModification: ToDto(tenantLayer.Modifications.GetValueOrDefault(definition.Name)));
    }

    private static bool Manageable(ISettingDefinition definition)
        => definition.Scopes.HasFlag(SettingScopes.Tenant) || definition.Scopes.HasFlag(SettingScopes.Host);

    private static SettingModificationDto? ToDto(SettingModification? modification)
        => modification is null
            ? null
            : new SettingModificationDto(modification.Time, modification.ModifierId, modification.ModifierName);

    private ISettingDefinition EnsureVisibleToClients(string name)
    {
        var definition = definitionManager.GetOrNull(name);
        if (definition is null || !definition.IsVisibleToClients)
        {
            throw new BusinessException(SettingErrorCodes.NotAvailable, $"Setting '{name}' is not available.")
                .WithData("Name", name);
        }

        return definition;
    }
}
