using Microsoft.Extensions.Logging;

namespace Leistd.Authorization;

/// <summary>
/// 权限定义管理器接口
/// </summary>
public interface IPermissionDefinitionManager
{
    /// <summary>
    /// 获取权限定义
    /// </summary>
    IPermissionDefinition? GetOrNull(string name);

    /// <summary>
    /// 获取所有权限定义
    /// </summary>
    IEnumerable<IPermissionDefinition> GetAll();

    /// <summary>
    /// 获取全部权限组，用于驱动权限管理界面的分组与树形展示。
    /// </summary>
    IReadOnlyList<IPermissionGroupDefinition> GetGroups();

    /// <summary>
    /// 判断权限是否已定义且实际可用。
    /// </summary>
    /// <remarks>
    /// 权限自身与其全部祖先都必须处于启用状态；定义不存在时返回 <c>false</c>，
    /// 使拼错或数据库残留的权限名默认被拒绝。
    /// </remarks>
    bool IsEffectivelyEnabled(string name);

    /// <summary>
    /// 获取权限的全部祖先名称，由近到远。定义不存在时返回空集合。
    /// </summary>
    /// <remarks>授予子权限时用它补齐祖先，使运行时无需回溯定义树。</remarks>
    IReadOnlyList<string> GetAncestorNames(string name);

    /// <summary>
    /// 获取权限的全部子孙名称。定义不存在时返回空集合。
    /// </summary>
    /// <remarks>撤销父权限时用它级联清理子孙授予。</remarks>
    IReadOnlyList<string> GetDescendantNames(string name);
}

/// <summary>
/// 权限定义管理器
/// 负责加载和管理所有权限定义
/// </summary>
/// <remarks>
/// 全部定义在构造时一次性加载，随后祖先链、子孙集合与有效启用状态都被预计算并缓存，
/// 因此运行期的权限检查与授予归一化都是字典查找，不发生定义树遍历。
/// 运行期新增或修改权限定义需重启进程。
/// </remarks>
public class PermissionDefinitionManager : IPermissionDefinitionManager
{
    private readonly PermissionDefinitionContext _context;
    private readonly ILogger<PermissionDefinitionManager> _logger;
    private readonly IReadOnlyList<IPermissionGroupDefinition> _groups;
    private readonly Dictionary<string, string[]> _ancestors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _descendants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _effectivelyEnabled = new(StringComparer.Ordinal);

    public PermissionDefinitionManager(
        IEnumerable<IPermissionDefinitionProvider> providers,
        ILogger<PermissionDefinitionManager> logger)
    {
        _context = new PermissionDefinitionContext();
        _logger = logger;

        LoadPermissionDefinitions(providers);

        _groups = _context.GetGroups().ToList();
        BuildRelationCaches();
    }

    private void LoadPermissionDefinitions(IEnumerable<IPermissionDefinitionProvider> providers)
    {
        _logger.LogInformation("开始加载权限定义...");

        var providerList = providers.ToList();
        _logger.LogInformation("找到 {Count} 个权限定义提供器", providerList.Count);

        foreach (var provider in providerList)
        {
            var providerType = provider.GetType().Name;
            try
            {
                _logger.LogDebug("加载权限定义提供器: {Provider}", providerType);
                provider.Define(_context);
                _logger.LogDebug("权限定义提供器 {Provider} 加载成功", providerType);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "加载权限定义提供器 {Provider} 失败", providerType);
                throw;
            }
        }

        var allPermissions = _context.GetAllPermissions().ToList();
        _logger.LogInformation("权限定义加载完成，共 {Count} 个权限", allPermissions.Count);
    }

    private void BuildRelationCaches()
    {
        foreach (var permission in _context.GetAllPermissions())
        {
            var ancestors = new List<string>();
            for (var parent = permission.Parent; parent != null; parent = parent.Parent)
            {
                ancestors.Add(parent.Name);
            }

            _ancestors[permission.Name] = [.. ancestors];

            var descendants = new List<string>();
            CollectDescendants(permission, descendants);
            _descendants[permission.Name] = [.. descendants];

            // 有效启用：自身与全部祖先都启用。
            if (permission.IsEnabled && IsAncestorChainEnabled(permission))
            {
                _effectivelyEnabled.Add(permission.Name);
            }
        }
    }

    private static void CollectDescendants(IPermissionDefinition permission, List<string> target)
    {
        foreach (var child in permission.Children)
        {
            target.Add(child.Name);
            CollectDescendants(child, target);
        }
    }

    private static bool IsAncestorChainEnabled(IPermissionDefinition permission)
    {
        for (var parent = permission.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.IsEnabled)
                return false;
        }

        return true;
    }

    public IPermissionDefinition? GetOrNull(string name)
    {
        return _context.GetPermissionOrNull(name);
    }

    public IEnumerable<IPermissionDefinition> GetAll()
    {
        return _context.GetAllPermissions();
    }

    public IReadOnlyList<IPermissionGroupDefinition> GetGroups()
    {
        return _groups;
    }

    public bool IsEffectivelyEnabled(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && _effectivelyEnabled.Contains(name);
    }

    public IReadOnlyList<string> GetAncestorNames(string name)
    {
        return _ancestors.TryGetValue(name, out var ancestors) ? ancestors : [];
    }

    public IReadOnlyList<string> GetDescendantNames(string name)
    {
        return _descendants.TryGetValue(name, out var descendants) ? descendants : [];
    }
}
