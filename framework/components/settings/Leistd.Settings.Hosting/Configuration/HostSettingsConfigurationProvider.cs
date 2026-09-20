using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Hosting.Configuration;

// 宿主级设置作为配置源：设置里有值的项覆盖部署配置的对应键，没有值的项不出现，自然回落到配置文件。
// 这是 .NET 让运行期可改的值流进 Options 的正规做法（自定义配置提供程序 + 重载令牌）：
// 数据变化时 OnReload，所有按配置节绑定的 IOptionsMonitor 随之重算，消费方不必知道值来自设置表。
internal sealed class HostSettingsConfigurationProvider : ConfigurationProvider
{
    // 写入后的立即应用与周期刷新可能同时到来，换数据与重载要成对完成
    private readonly Lock _gate = new();
    private Dictionary<string, string?>? _rejected;

    public bool IsAttached { get; private set; }

    public void MarkAttached() => IsAttached = true;

    // 换上新的一组配置键值；新值让某个 Options 校验不过时整组不生效，沿用上一组。
    // 校验失败有两处来源：订阅了变更的 IOptionsMonitor 在重载回调里就重算并抛出；没人订阅的由 validate 显式检验。
    // 这时换回上一组再重载一次，消费方始终取到合规的值。成对的项（发信账号与口令）只设了一半时停在上一组，设齐后整组生效。
    // 与当前一组或上次被拒的一组相同时什么都不做：周期刷新既不无谓地重算，也不每轮重复报告同一个错误。
    public IReadOnlyList<string> Apply(IReadOnlyDictionary<string, string?> values, Func<IReadOnlyList<string>> validate)
    {
        lock (_gate)
        {
            if (Matches(Data, values) || (_rejected is not null && Matches(_rejected, values)))
            {
                return [];
            }

            var previous = Data;
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            var failures = Reload();
            if (failures.Count == 0)
            {
                failures = [.. validate()];
            }

            if (failures.Count == 0)
            {
                _rejected = null;
                return [];
            }

            Data = previous;
            _rejected = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            // 换回的上一组若也不合规，是部署配置本身有误，不是这里能纠正的
            _ = Reload();
            return failures;
        }
    }

    // 重载并收集 Options 的校验失败；其它异常照常抛出
    private IReadOnlyList<string> Reload()
    {
        try
        {
            OnReload();
            return [];
        }
        // 重载回调逐层包 AggregateException，展平后再判断
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            return [.. ex.Flatten().InnerExceptions.Select(inner => inner.Message)];
        }
        catch (OptionsValidationException ex)
        {
            return [ex.Message];
        }
    }

    private static bool Matches(IDictionary<string, string?> current, IReadOnlyDictionary<string, string?> values) =>
        current.Count == values.Count &&
        values.All(pair => current.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

// 只产出同一个提供程序实例：应用器往 DI 里那个实例推数据
internal sealed class HostSettingsConfigurationSource(HostSettingsConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        provider.MarkAttached();
        return provider;
    }
}
