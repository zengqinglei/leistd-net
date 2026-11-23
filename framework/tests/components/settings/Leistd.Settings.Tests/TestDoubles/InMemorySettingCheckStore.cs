using Leistd.Settings.Checks;

namespace Leistd.Settings.Tests.TestDoubles;

// 单租户的内存测试结果存储：保存即覆盖并清除过期标记，标记过期只作用于已有结果
internal sealed class InMemorySettingCheckStore : ISettingCheckStore
{
    private readonly Dictionary<string, SettingCheckResult> _results = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, SettingCheckResult>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            return Task.FromResult<IReadOnlyDictionary<string, SettingCheckResult>>(
                new Dictionary<string, SettingCheckResult>(_results, StringComparer.Ordinal));
        }
    }

    public Task SaveAsync(string settingName, SettingCheckResult result, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            _results[settingName] = result with { IsStale = false };
        }

        return Task.CompletedTask;
    }

    public Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            foreach (var name in settingNames)
            {
                if (_results.TryGetValue(name, out var result))
                {
                    _results[name] = result with { IsStale = true };
                }
            }
        }

        return Task.CompletedTask;
    }
}
