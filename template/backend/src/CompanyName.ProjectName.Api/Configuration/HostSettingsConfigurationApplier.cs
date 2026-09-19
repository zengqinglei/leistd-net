using CompanyName.ProjectName.Application.Settings.Hosting;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>
/// 把宿主级设置推进 <see cref="HostSettingsConfigurationProvider"/>，按 <see cref="HostSettingBindings"/> 换成配置键。
/// </summary>
/// <remarks>
/// <para>只推<b>设过</b>的项：是否设过看存储里有没有那一行（没设的项不进这个配置源，由配置文件、环境变量给出）；
/// 值经 <see cref="ISettingProvider"/> 取，机密设置由它解密——明文只在进程内的配置里，与环境变量、密钥库给出的
/// 凭据一样，库里仍是密文。写入入口会作废同一作用域的读取缓存，"写完立即应用"读到的就是新值。</para>
/// <para>新值让某个 Options 校验不过时整组不生效、沿用上一组，只记错误不抛：值已经落库，
/// 抛出只会让保存设置的请求或周期刷新失败。写入端逐项校验，这里拦下的是单项合规、组合起来不合规的情形。</para>
/// </remarks>
/// <param name="configuration">宿主级设置配置源。</param>
/// <param name="settingStore">判断宿主层哪些项设过。</param>
/// <param name="settingProvider">取设过的项的值（机密设置已解密）。</param>
/// <param name="logger">报告被拒的设置。</param>
public sealed class HostSettingsConfigurationApplier(
    HostSettingsConfigurationProvider configuration,
    ISettingStore settingStore,
    ISettingProvider settingProvider,
    ILogger<HostSettingsConfigurationApplier> logger) : IHostSettingApplier
{
    /// <inheritdoc />
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settingStore.GetAllAsync(SettingScopes.Host, userId: null, cancellationToken);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in HostSettingBindings.All.Where(binding => stored.ContainsKey(binding.SettingName)))
        {
            var value = await settingProvider.GetOrNullAsync(binding.SettingName, cancellationToken);
            foreach (var key in binding.ConfigurationKeys)
            {
                values[key] = value;
            }
        }

        var failures = configuration.Apply(values);
        if (failures.Count > 0)
        {
            logger.LogError(
                "Host settings were not applied because some options would fail validation; keeping the previous values: {Failures}",
                string.Join("; ", failures));
        }
    }
}
