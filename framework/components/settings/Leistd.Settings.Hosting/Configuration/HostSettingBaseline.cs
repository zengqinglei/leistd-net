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
    // 与 IConfiguration 取值同一语义：优先级最高、含绑定键的配置源说了算，显式空值也算（部署有意置空）；
    // 每次现读，部署配置重载后即是新值
    public string? GetDeploymentValue(string settingName)
    {
        var binding = bindings.Value.Bindings.FirstOrDefault(item => item.SettingName == settingName);
        if (binding is null)
        {
            return null;
        }

        if (configuration is not IConfigurationRoot root)
        {
            return binding.ConfigurationKeys.Select(key => configuration[key]).FirstOrDefault(value => value is not null);
        }

        foreach (var provider in root.Providers.Where(provider => !ReferenceEquals(provider, hostSettings)).Reverse())
        {
            foreach (var key in binding.ConfigurationKeys)
            {
                if (provider.TryGet(key, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    // 代码默认值用：跳过空值，找第一个有内容的部署值
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
