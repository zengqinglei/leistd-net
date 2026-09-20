using Leistd.OperationRecords.Abstractions;

namespace Leistd.OperationRecords.Services;

// 操作动作定义
internal sealed class OperationActionDefinition(
    string code,
    string category,
    OperationVisibility visibility,
    OperationSeverity severity,
    bool targetIsActor) : IOperationActionDefinition
{
    public string Code { get; } = code;
    public string Category { get; } = category;
    public OperationVisibility Visibility { get; } = visibility;
    public OperationSeverity Severity { get; } = severity;
    public bool TargetIsActor { get; } = targetIsActor;
}

// 动作码到定义的全局注册表，保证动作码唯一并提供 O(1) 查找。
// 与 PermissionDefinitionRegistry 同型：重复即抛，让冲突死在启动期而不是运行期。
internal sealed class OperationActionDefinitionRegistry
{
    private readonly Dictionary<string, OperationActionDefinition> _actions = new(StringComparer.Ordinal);
    private readonly List<OperationActionDefinition> _ordered = [];

    public void Register(OperationActionDefinition action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Code, nameof(action));
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Category, nameof(action));

        if (!_actions.TryAdd(action.Code, action))
        {
            throw new InvalidOperationException(
                $"Operation action '{action.Code}' already exists; action codes must be globally unique.");
        }

        _ordered.Add(action);
    }

    public OperationActionDefinition? GetOrNull(string code)
        => _actions.TryGetValue(code, out var action) ? action : null;

    // 保持登记顺序：界面按它渲染筛选项，字典序会让"创建/删除/更新"这种排法读起来很别扭。
    public IReadOnlyList<OperationActionDefinition> GetAll() => _ordered;
}

// 操作动作定义上下文
internal sealed class OperationActionDefinitionContext : IOperationActionDefinitionContext
{
    private readonly OperationActionDefinitionRegistry _registry = new();

    public IOperationActionDefinition Add(
        string code,
        string category,
        OperationVisibility visibility,
        OperationSeverity severity = OperationSeverity.Info,
        bool targetIsActor = false)
    {
        var action = new OperationActionDefinition(code, category, visibility, severity, targetIsActor);
        _registry.Register(action);
        return action;
    }

    public IOperationActionDefinition? GetOrNull(string code)
        => string.IsNullOrWhiteSpace(code) ? null : _registry.GetOrNull(code);

    internal IReadOnlyList<OperationActionDefinition> GetAll() => _registry.GetAll();
}
