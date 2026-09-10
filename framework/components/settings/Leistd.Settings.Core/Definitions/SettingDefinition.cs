using Leistd.Settings.Abstractions;

namespace Leistd.Settings.Definitions;

// 设置定义
internal sealed class SettingDefinition : ISettingDefinition
{
    public SettingDefinition(
        string name,
        string? defaultValue,
        SettingScopes scopes,
        string? displayName,
        string? group)
    {
        // 进程级与可分层覆盖是互斥的两种东西：Host | User 这种组合没有一致的解释——
        // 读取要么按宿主那一行、要么按用户覆盖，两者不可能同时成立。文档已把它定为互斥，
        // 那就在构造处拒绝，而不是留给每个消费者各自解释。
        if (scopes.HasFlag(SettingScopes.Host) && scopes != SettingScopes.Host)
        {
            throw new ArgumentException(
                $"Setting '{name}' declares '{scopes}': the host scope is exclusive and cannot be "
                + "combined with the tenant or user scope.",
                nameof(scopes));
        }

        Name = name;
        DefaultValue = defaultValue;
        Scopes = scopes;
        DisplayName = displayName;
        Group = group;
    }

    public string Name { get; }
    public string? DisplayName { get; set; }
    public string? DefaultValue { get; set; }
    public SettingScopes Scopes { get; }
    public string? Group { get; set; }
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
        string? displayName = null,
        string? group = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var definition = new SettingDefinition(name, defaultValue, scopes, displayName, group);
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
