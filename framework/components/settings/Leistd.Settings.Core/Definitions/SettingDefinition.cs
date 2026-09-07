using Leistd.Settings.Abstractions;

namespace Leistd.Settings.Definitions;

// 设置定义
internal sealed class SettingDefinition(
    string name,
    string? defaultValue,
    SettingScopes scopes,
    string? displayName) : ISettingDefinition
{
    public string Name { get; } = name;
    public string? DisplayName { get; set; } = displayName;
    public string? DefaultValue { get; set; } = defaultValue;
    public SettingScopes Scopes { get; } = scopes;
    public bool IsVisibleToClients { get; set; }
}

// 设置定义上下文：名称全局唯一，重复注册在首次访问定义时失败。
internal sealed class SettingDefinitionContext : ISettingDefinitionContext
{
    private readonly Dictionary<string, SettingDefinition> _settings = new(StringComparer.Ordinal);

    public ISettingDefinition Add(
        string name,
        string? defaultValue = null,
        SettingScopes scopes = SettingScopes.Tenant,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var definition = new SettingDefinition(name, defaultValue, scopes, displayName);
        if (!_settings.TryAdd(name, definition))
        {
            throw new InvalidOperationException(
                $"Setting '{name}' already exists; setting names must be globally unique.");
        }

        return definition;
    }

    public ISettingDefinition? GetOrNull(string name)
        => string.IsNullOrWhiteSpace(name) ? null : _settings.GetValueOrDefault(name);

    public IReadOnlyList<ISettingDefinition> GetAll() => [.. _settings.Values];
}
