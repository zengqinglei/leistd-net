using Leistd.Settings.Hosting.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Hosting.Configuration;

// 不能从 IConfiguration 现取（会读到设置覆盖后的值），因此逐个配置提供程序查值并跳过宿主设置那一个；
// 后加入的配置源优先，同一配置源内按键的顺序取
internal sealed class HostSettingBaseline(
    IConfiguration configuration,
    HostSettingsConfigurationProvider hostSettings,
    IOptions<HostSettingBindingCollection> bindings) : IHostSettingBaseline
{
    public string? GetDeploymentValue(string settingName)
    {
        var binding = bindings.Value.Bindings.FirstOrDefault(item => item.SettingName == settingName);
        return binding is null ? null : Lookup(binding);
    }

    internal string? Lookup(HostSettingBinding binding)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return binding.ConfigurationKeys.Select(key => configuration[key]).FirstOrDefault(value => !string.IsNullOrEmpty(value));
        }

        foreach (var provider in root.Providers.Where(provider => !ReferenceEquals(provider, hostSettings)).Reverse())
        {
            foreach (var key in binding.ConfigurationKeys)
            {
                if (provider.TryGet(key, out var value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
