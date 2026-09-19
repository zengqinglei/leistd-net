using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>
/// 把宿主级设置作为配置源：设置里有值的项覆盖部署配置的对应键，没有值的项不出现，自然回落到配置文件。
/// </summary>
/// <remarks>
/// <para>这是 .NET 让运行期可改的值流进 Options 的正规做法（自定义配置提供程序 + 重载令牌）：
/// 数据变化时调用 <see cref="ConfigurationProvider.OnReload"/>，所有按配置节绑定的 <c>IOptionsMonitor&lt;T&gt;</c>
/// 随之重算，消费方不必知道值来自设置表，也不必为每个 Options 另写一套提供方。</para>
/// <para>数据由 <see cref="HostSettingsConfigurationApplier"/> 推入：宿主开始接收请求之前推入一次，
/// 写入设置的事务提交后本进程立即再推，其它实例由周期刷新跟上。</para>
/// </remarks>
public sealed class HostSettingsConfigurationProvider : ConfigurationProvider
{
    // 写入后的立即应用与周期刷新可能同时到来，换数据与重载要成对完成
    private readonly Lock _gate = new();
    private Dictionary<string, string?>? _rejected;

    /// <summary>
    /// 换上新的一组配置键值；新值让某个 Options 校验不过时整组不生效，沿用上一组。
    /// </summary>
    /// <remarks>
    /// <para><c>IOptionsMonitor</c> 在重载回调里就重算并校验，校验异常从 <see cref="ConfigurationProvider.OnReload"/> 冒出来。
    /// 这时换回上一组再重载一次：消费方始终取到合规的值，而不是在有人改正之前每次取值都抛异常。
    /// 要成对设置的项（如发信账号与口令）只设了一半时就停在上一组，设齐后整组生效。</para>
    /// <para>与当前一组相同、或与上次被拒的一组相同时什么都不做：周期刷新既不会无谓地让所有 Options 重算，
    /// 也不会每一轮都重复报告同一个错误。</para>
    /// </remarks>
    /// <param name="values">配置键 → 值。</param>
    /// <returns>被拒时的校验失败信息；已生效或无需变更时为空。</returns>
    public IReadOnlyList<string> Apply(IReadOnlyDictionary<string, string?> values)
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
    private List<string> Reload()
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
    }

    private static bool Matches(IDictionary<string, string?> current, IReadOnlyDictionary<string, string?> values) =>
        current.Count == values.Count &&
        values.All(pair => current.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

/// <summary>
/// <see cref="HostSettingsConfigurationProvider"/> 的配置源，只产出同一个提供程序实例。
/// </summary>
/// <param name="provider">唯一的提供程序实例，同时注册进 DI 供应用器推数据。</param>
public sealed class HostSettingsConfigurationSource(HostSettingsConfigurationProvider provider) : IConfigurationSource
{
    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary>宿主级设置配置源的注册入口。</summary>
public static class HostSettingsConfigurationExtensions
{
    /// <summary>
    /// 把宿主级设置作为配置源加在已有配置源之后，优先级最高。
    /// </summary>
    /// <remarks>
    /// 要在 <c>builder.Build()</c> <b>之后</b>调用：构建期间还会追加配置源（例如测试宿主的覆盖配置），
    /// 在那之前加入的话它们会排在后面、压过设置。配置在构建之后仍可追加，追加即触发一次重载。
    /// </remarks>
    /// <param name="configuration">应用配置。</param>
    /// <param name="provider">已注册进 DI 的那个提供程序实例（应用器往它推数据）。</param>
    public static IConfigurationBuilder AddHostSettings(
        this IConfigurationBuilder configuration,
        HostSettingsConfigurationProvider provider)
        => configuration.Add(new HostSettingsConfigurationSource(provider));
}
