using System.Globalization;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Hosting.Configuration;
using Leistd.Settings.Hosting.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Hosting.Definitions;

// 被绑定设置的代码默认值 = 部署基线。默认值不能从 IConfiguration 现取：设置加载进配置之后读到的就是覆盖后的值，
// 界面上的默认值随之变成当前值，"重置"也回不到部署基线。因此逐个配置提供程序查值并跳过宿主设置那一个；
// 取值规则与 IConfiguration 一致——后加入的配置源优先，同一配置源内按键的顺序取。
internal sealed class HostSettingDefaultsProvider(
    IConfiguration configuration,
    HostSettingsConfigurationProvider hostSettings,
    IOptions<HostSettingBindingCollection> bindings) : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
    }

    public void PostDefine(ISettingDefinitionContext context)
    {
        var deployment = configuration is IConfigurationRoot root
            ? root.Providers.Where(provider => !ReferenceEquals(provider, hostSettings)).Reverse().ToList()
            : null;

        foreach (var binding in bindings.Value.Bindings)
        {
            var definition = context.GetOrNull(binding.SettingName)
                ?? throw new InvalidOperationException(
                    $"Host setting binding '{binding.SettingName}' refers to a setting that is not defined.");
            if (definition.Scopes != SettingScopes.Host)
            {
                throw new InvalidOperationException(
                    $"Setting '{binding.SettingName}' is bound to configuration but is not host-scoped; only process-wide settings can override deployment configuration.");
            }

            // 口令不该作为默认值下发到界面
            if (definition.IsEncrypted)
            {
                definition.DefaultValue = null;
                continue;
            }

            definition.DefaultValue = Normalize(definition, Lookup(deployment, binding))
                ?? Normalize(definition, binding.Fallback);
        }
    }

    private string? Lookup(IReadOnlyList<IConfigurationProvider>? deployment, HostSettingBinding binding)
    {
        if (deployment is null)
        {
            return binding.ConfigurationKeys.Select(key => configuration[key]).FirstOrDefault(value => !string.IsNullOrEmpty(value));
        }

        foreach (var provider in deployment)
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

    // 归一到设置值的写法：设置值按序号比对，配置里写成 "warning"、"True" 也要对得上；
    // 认不出的取值返回 null，由调用方退回兜底值，而不是下发一个连写入端都不接受的默认值
    private static string? Normalize(ISettingDefinition definition, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (definition.AllowedValues is { } allowed)
        {
            return allowed.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        }

        return definition.ValueType switch
        {
            SettingValueType.Boolean => bool.TryParse(value, out var flag) ? (flag ? "true" : "false") : null,
            SettingValueType.Integer => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : null,
            _ => value,
        };
    }
}
