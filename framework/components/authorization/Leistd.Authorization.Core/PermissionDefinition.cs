namespace Leistd.Authorization;

/// <summary>
/// 权限定义
/// </summary>
internal sealed class PermissionDefinition : IPermissionDefinition
{
    private readonly List<PermissionDefinition> _children = [];
    private readonly PermissionDefinitionRegistry _registry;

    public string Name { get; }
    public string? DisplayName { get; set; }
    public IPermissionDefinition? Parent { get; }
    public IReadOnlyList<IPermissionDefinition> Children => _children;
    public bool IsEnabled { get; set; } = true;

    internal PermissionDefinition(
        PermissionDefinitionRegistry registry,
        string name,
        string? displayName = null,
        PermissionDefinition? parent = null)
    {
        _registry = registry;
        Name = name;
        DisplayName = displayName;
        Parent = parent;
    }

    public IPermissionDefinition AddChild(string name, string? displayName = null)
    {
        var child = new PermissionDefinition(_registry, name, displayName, this);
        _registry.Register(child);
        _children.Add(child);
        return child;
    }
}

/// <summary>
/// 权限组定义
/// </summary>
internal sealed class PermissionGroupDefinition : IPermissionGroupDefinition
{
    private readonly List<PermissionDefinition> _permissions = [];
    private readonly PermissionDefinitionRegistry _registry;

    public string Name { get; }
    public string? DisplayName { get; set; }

    public IReadOnlyList<IPermissionDefinition> Permissions => _permissions;

    internal PermissionGroupDefinition(PermissionDefinitionRegistry registry, string name, string? displayName = null)
    {
        _registry = registry;
        Name = name;
        DisplayName = displayName;
    }

    public IPermissionDefinition AddPermission(string name, string? displayName = null)
    {
        var permission = new PermissionDefinition(_registry, name, displayName);
        _registry.Register(permission);
        _permissions.Add(permission);
        return permission;
    }

    public IPermissionDefinition? GetPermissionOrNull(string name)
    {
        var permission = _registry.GetOrNull(name);
        return permission != null && BelongsToThisGroup(permission) ? permission : null;
    }

    internal IEnumerable<PermissionDefinition> GetAllPermissions()
    {
        foreach (var permission in _permissions)
        {
            yield return permission;

            foreach (var child in Flatten(permission))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<PermissionDefinition> Flatten(PermissionDefinition permission)
    {
        foreach (var child in permission.Children.Cast<PermissionDefinition>())
        {
            yield return child;

            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private bool BelongsToThisGroup(PermissionDefinition permission)
    {
        var root = permission;
        while (root.Parent is PermissionDefinition parent)
        {
            root = parent;
        }

        return _permissions.Contains(root);
    }
}

/// <summary>
/// 权限名称到定义的全局注册表，保证权限名在所有组与层级中唯一，并提供 O(1) 查找。
/// </summary>
internal sealed class PermissionDefinitionRegistry
{
    private readonly Dictionary<string, PermissionDefinition> _permissions = new(StringComparer.Ordinal);

    public void Register(PermissionDefinition permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission.Name, nameof(permission));

        if (!_permissions.TryAdd(permission.Name, permission))
            throw new InvalidOperationException($"权限 '{permission.Name}' 已存在，权限名称必须全局唯一。");
    }

    public PermissionDefinition? GetOrNull(string name)
        => _permissions.TryGetValue(name, out var permission) ? permission : null;
}

/// <summary>
/// 权限定义上下文
/// </summary>
internal sealed class PermissionDefinitionContext : IPermissionDefinitionContext
{
    private readonly Dictionary<string, PermissionGroupDefinition> _groups = new(StringComparer.Ordinal);
    private readonly PermissionDefinitionRegistry _registry = new();

    public IPermissionGroupDefinition GetOrAddGroup(string name, string? displayName = null)
    {
        if (!_groups.TryGetValue(name, out var group))
        {
            group = new PermissionGroupDefinition(_registry, name, displayName);
            _groups[name] = group;
        }
        else if (displayName != null && group.DisplayName != displayName)
        {
            group.DisplayName = displayName;
        }

        return group;
    }

    public IPermissionDefinition? GetPermissionOrNull(string name)
        => string.IsNullOrWhiteSpace(name) ? null : _registry.GetOrNull(name);

    public IEnumerable<IPermissionGroupDefinition> GetGroups() => _groups.Values;

    public IEnumerable<IPermissionDefinition> GetAllPermissions()
        => _groups.Values.SelectMany(group => group.GetAllPermissions());
}
