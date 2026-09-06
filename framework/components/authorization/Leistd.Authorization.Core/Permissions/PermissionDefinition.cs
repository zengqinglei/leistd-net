using Leistd.MultiTenancy;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.Permissions;

// 权限定义
internal sealed class PermissionDefinition : IPermissionDefinition
{
    private readonly List<PermissionDefinition> _children = [];
    private readonly PermissionDefinitionRegistry _registry;

    public string Name { get; }
    public string? DisplayName { get; set; }
    public IPermissionDefinition? Parent { get; }
    public IReadOnlyList<IPermissionDefinition> Children => _children;
    public bool IsEnabled { get; set; } = true;
    public MultiTenancySides Side { get; }

    internal PermissionDefinition(
        PermissionDefinitionRegistry registry,
        string name,
        string? displayName = null,
        PermissionDefinition? parent = null,
        MultiTenancySides side = MultiTenancySides.Both)
    {
        _registry = registry;
        Name = name;
        DisplayName = displayName;
        Parent = parent;
        Side = side;
    }

    public IPermissionDefinition AddChild(string name, string? displayName = null, MultiTenancySides? side = null)
    {
        // 子权限默认继承父权限侧别：宿主侧资源的动作权限不必逐个声明
        var child = new PermissionDefinition(_registry, name, displayName, this, side ?? Side);
        _registry.Register(child);
        _children.Add(child);
        return child;
    }
}

// 权限组定义
internal sealed class PermissionGroupDefinition : IPermissionGroupDefinition
{
    private readonly List<PermissionDefinition> _permissions = [];
    private readonly PermissionDefinitionRegistry _registry;

    public string Name { get; }
    public string? DisplayName { get; set; }
    public MultiTenancySides Side { get; }

    public IReadOnlyList<IPermissionDefinition> Permissions => _permissions;

    internal PermissionGroupDefinition(
        PermissionDefinitionRegistry registry,
        string name,
        string? displayName = null,
        MultiTenancySides side = MultiTenancySides.Both)
    {
        _registry = registry;
        Name = name;
        DisplayName = displayName;
        Side = side;
    }

    public IPermissionDefinition AddPermission(string name, string? displayName = null, MultiTenancySides? side = null)
    {
        // 权限默认继承组侧别
        var permission = new PermissionDefinition(_registry, name, displayName, parent: null, side ?? Side);
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

// 权限名称到定义的全局注册表，保证权限名在所有组与层级中唯一，并提供 O(1) 查找。
internal sealed class PermissionDefinitionRegistry
{

    private readonly Dictionary<string, PermissionDefinition> _permissions = new(StringComparer.Ordinal);

    public void Register(PermissionDefinition permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission.Name, nameof(permission));

        // '|' 是"任一满足"策略名的分隔符：名字里带它的权限会在解析时被拆开，
        // 每一段都找不到定义，最终以"策略不存在"的形式失败，排查成本很高。
        if (permission.Name.Contains(PermissionPolicyNames.AnyOfSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Permission '{permission.Name}' contains the reserved character '{PermissionPolicyNames.AnyOfSeparator}', "
                + "which separates multiple permission names in an any-of policy.");
        }

        if (!_permissions.TryAdd(permission.Name, permission))
            throw new InvalidOperationException($"Permission '{permission.Name}' already exists; permission names must be globally unique.");
    }

    public PermissionDefinition? GetOrNull(string name)
        => _permissions.TryGetValue(name, out var permission) ? permission : null;
}

// 权限定义上下文
internal sealed class PermissionDefinitionContext : IPermissionDefinitionContext
{
    private readonly Dictionary<string, PermissionGroupDefinition> _groups = new(StringComparer.Ordinal);
    private readonly PermissionDefinitionRegistry _registry = new();

    public IPermissionGroupDefinition GetOrAddGroup(string name, string? displayName = null, MultiTenancySides side = MultiTenancySides.Both)
    {
        if (!_groups.TryGetValue(name, out var group))
        {
            group = new PermissionGroupDefinition(_registry, name, displayName, side);
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
