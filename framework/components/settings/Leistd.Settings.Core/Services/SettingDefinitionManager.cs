using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;

namespace Leistd.Settings.Services;

/// <summary>
/// 首次访问时汇总全部 <see cref="ISettingDefinitionProvider"/> 的定义。
/// </summary>
/// <remarks>
/// 定义在进程生命周期内不变，因此一次性构建后只读；名称冲突在这里就地失败，
/// 而不是留到某次读取时才表现为"读到了别人的默认值"。
/// </remarks>
/// <param name="providers">已注册的定义提供者。</param>
public sealed class SettingDefinitionManager(IEnumerable<ISettingDefinitionProvider> providers) : ISettingDefinitionManager
{
    private readonly Lazy<SettingDefinitionContext> _context = new(() =>
    {
        var context = new SettingDefinitionContext();
        foreach (var provider in providers)
        {
            provider.Define(context);
        }
        return context;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public ISettingDefinition? GetOrNull(string name) => _context.Value.GetOrNull(name);

    /// <inheritdoc />
    public IReadOnlyList<ISettingDefinition> GetAll() => _context.Value.GetAll();
}
