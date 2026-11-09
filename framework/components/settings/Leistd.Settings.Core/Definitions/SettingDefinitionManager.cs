namespace Leistd.Settings.Definitions;

/// <summary>首次访问时汇总全部 <see cref="ISettingDefinitionProvider"/> 的定义。</summary>
/// <remarks>定义在进程生命周期内不变，一次性构建后只读；名称冲突在构建时抛出。</remarks>
/// <param name="providers">已注册的定义提供者。</param>
public sealed class SettingDefinitionManager(IEnumerable<ISettingDefinitionProvider> providers) : ISettingDefinitionManager
{
    private readonly Lazy<SettingDefinitionContext> _context = new(() =>
    {
        var context = new SettingDefinitionContext();
        var all = providers.ToList();
        foreach (var provider in all)
        {
            provider.Define(context);
        }

        foreach (var provider in all)
        {
            provider.PostDefine(context);
        }

        ValidateShared(context.GetAll());
        return context;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // 共享端点按租户下发明文生效值：机密值不能离开本服务，进程级设置没有租户维度，只有用户层的设置没有租户值
    private static void ValidateShared(IReadOnlyList<ISettingDefinition> definitions)
    {
        var invalid = definitions
            .Where(definition => definition.IsShared)
            .Select(definition => definition.IsEncrypted ? $"'{definition.Name}' is encrypted"
                : definition.Scopes.HasFlag(SettingScopes.Host) ? $"'{definition.Name}' is a host-scope setting"
                : !definition.Scopes.HasFlag(SettingScopes.Tenant) ? $"'{definition.Name}' does not allow the tenant scope"
                : null)
            .OfType<string>()
            .ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                "Only non-encrypted settings that allow the tenant scope can be shared with other services: "
                + string.Join("; ", invalid) + ".");
        }
    }

    /// <inheritdoc />
    public ISettingDefinition? GetOrNull(string name) => _context.Value.GetOrNull(name);

    /// <inheritdoc />
    public IReadOnlyList<ISettingDefinition> GetAll() => _context.Value.GetAll();
}
