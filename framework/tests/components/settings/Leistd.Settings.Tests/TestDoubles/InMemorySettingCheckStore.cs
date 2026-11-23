using Leistd.Settings.Checks;

namespace Leistd.Settings.Tests.TestDoubles;

// 单租户的内存测试结果存储：保存即覆盖（过期标记原样保存）；标记过期作用于已有结果并作废运行登记
internal sealed class InMemorySettingCheckStore : ISettingCheckStore
{
    private readonly Dictionary<string, SettingCheckResult> _results = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _runs = new(StringComparer.Ordinal);

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
            _results[settingName] = result;
            _runs.Remove(settingName);
        }

        return Task.CompletedTask;
    }

    public Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            foreach (var name in settingNames)
            {
                _runs.Remove(name);
                if (_results.TryGetValue(name, out var result))
                {
                    _results[name] = result with { IsStale = true };
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<Guid> BeginRunAsync(string settingName, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        lock (_results)
        {
            _runs[settingName] = runId;
        }

        return Task.FromResult(runId);
    }

    public Task<Guid?> GetPendingRunAsync(string settingName, CancellationToken cancellationToken = default)
    {
        lock (_results)
        {
            return Task.FromResult<Guid?>(_runs.TryGetValue(settingName, out var runId) ? runId : null);
        }
    }
}
