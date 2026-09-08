using System.ComponentModel;
using System.Globalization;
using Leistd.Security.Users;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;

namespace Leistd.Settings.Services;

/// <summary>
/// 按 用户级 → 租户级 → 代码默认值 的顺序解析设置。
/// </summary>
/// <remarks>
/// 按作用域缓存已读取的设置，同一作用域内保持一致；跨作用域重新读取，不使用分布式缓存。
/// </remarks>
/// <param name="definitionManager">设置定义。</param>
/// <param name="store">设置值存储。</param>
/// <param name="currentUser">当前用户；匿名时只回落到租户级。</param>
public sealed class DefaultSettingProvider(
    ISettingDefinitionManager definitionManager,
    ISettingStore store,
    ICurrentUser currentUser) : ISettingProvider
{
    private IReadOnlyDictionary<string, string>? _tenantValues;
    private IReadOnlyDictionary<string, string>? _userValues;

    /// <inheritdoc />
    public async Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        var definition = definitionManager.GetOrNull(name) ?? throw new UndefinedSettingException(name);

        await LoadAsync(cancellationToken);

        return Resolve(definition);
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var value = await GetOrNullAsync(name, cancellationToken);
        return string.IsNullOrEmpty(value) ? default : Convert<T>(value);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
        bool visibleToClientsOnly = false,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);

        return definitionManager.GetAll()
            .Where(definition => !visibleToClientsOnly || definition.IsVisibleToClients)
            .ToDictionary(definition => definition.Name, Resolve, StringComparer.Ordinal);
    }

    private string? Resolve(ISettingDefinition definition)
    {
        if (definition.Scopes.HasFlag(SettingScopes.User)
            && _userValues!.TryGetValue(definition.Name, out var userValue))
        {
            return userValue;
        }

        if (definition.Scopes.HasFlag(SettingScopes.Tenant)
            && _tenantValues!.TryGetValue(definition.Name, out var tenantValue))
        {
            return tenantValue;
        }

        return definition.DefaultValue;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_tenantValues is not null)
        {
            return;
        }

        // 两次查询成功后再一起发布缓存，避免失败重试读到半初始化状态。
        var tenantValues = await store.GetAllAsync(SettingScopes.Tenant, userId: null, cancellationToken);

        var userId = currentUser.Id?.ToString();
        var userValues = userId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await store.GetAllAsync(SettingScopes.User, userId, cancellationToken);

        _tenantValues = tenantValues;
        _userValues = userValues;
    }

    private static T Convert<T>(string value)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (target.IsEnum)
        {
            return (T)Enum.Parse(target, value, ignoreCase: true);
        }

        // TypeDescriptor 覆盖基元类型与带 TypeConverter 的自定义类型；
        // 固定用不变文化，否则同一份值在不同区域设置的节点上会解析出不同结果。
        return (T)TypeDescriptor.GetConverter(target).ConvertFromString(null, CultureInfo.InvariantCulture, value)!;
    }
}
