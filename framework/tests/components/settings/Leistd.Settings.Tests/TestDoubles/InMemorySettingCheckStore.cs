using Leistd.Settings.Checks;

namespace Leistd.Settings.Tests.TestDoubles;

// 单租户的内存测试结果存储：保存即覆盖（过期标记原样保存）；标记过期作用于已有结果并推进配置修订号
internal sealed class InMemorySettingCheckStore : ISettingCheckStore
{
    private readonly Dictionary<string, SettingCheckResult> _results = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Allocated, long Saved, long Revision)> _counters = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, SettingCheckResult>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            return Task.FromResult<IReadOnlyDictionary<string, SettingCheckResult>>(
                new Dictionary<string, SettingCheckResult>(_results, StringComparer.Ordinal));
        }
    }

    public Task SaveAsync(string settingName, long sequence, SettingCheckResult result, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            _results[settingName] = result;
            var counters = _counters.GetValueOrDefault(settingName);
            _counters[settingName] = counters with { Saved = sequence };
        }

        return Task.CompletedTask;
    }

    public Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            foreach (var name in settingNames)
            {
                if (_counters.TryGetValue(name, out var counters))
                {
                    _counters[name] = counters with { Revision = counters.Revision + 1 };
                }

                if (_results.TryGetValue(name, out var result))
                {
                    _results[name] = result with { IsStale = true };
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<SettingCheckRun> BeginRunAsync(string settingName, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            var counters = _counters.GetValueOrDefault(settingName);
            counters = counters with { Allocated = counters.Allocated + 1 };
            _counters[settingName] = counters;
            return Task.FromResult(new SettingCheckRun(counters.Allocated, counters.Revision));
        }
    }

    public Task<SettingCheckRun> GetLatestAsync(string settingName, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            var counters = _counters.GetValueOrDefault(settingName);
            return Task.FromResult(new SettingCheckRun(counters.Saved, counters.Revision));
        }
    }
}
