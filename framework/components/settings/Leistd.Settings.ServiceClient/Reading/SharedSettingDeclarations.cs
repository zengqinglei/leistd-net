namespace Leistd.Settings.ServiceClient.Reading;

/// <summary>使用方要读取的共享设置及各自读不到时的兜底值。</summary>
public sealed class SharedSettingDeclarations
{
    private readonly Dictionary<string, string?> _fallbacks = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string?> Fallbacks => _fallbacks;

    /// <summary>声明一项要读取的共享设置。</summary>
    /// <remarks>兜底值按设置的语义选：安全策略取更严格的一侧，显示类取部署基线；框架不拿本地默认值顶替。</remarks>
    /// <param name="name">设置名，与提供方定义的一致。</param>
    /// <param name="fallback">读不到时的值。</param>
    /// <returns>本声明集，便于链式声明。</returns>
    /// <exception cref="InvalidOperationException">同一设置名重复声明。</exception>
    public SharedSettingDeclarations Add(string name, string? fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_fallbacks.TryAdd(name, fallback))
        {
            throw new InvalidOperationException($"Shared setting '{name}' is already declared for this provider.");
        }

        return this;
    }
}
