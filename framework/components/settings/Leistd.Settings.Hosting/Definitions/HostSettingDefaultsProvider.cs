using System.Globalization;
using Leistd.Settings.Definitions;
using Leistd.Settings.Hosting.Configuration;
using Leistd.Settings.Hosting.Options;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Hosting.Definitions;

// 被绑定设置的代码默认值 = 部署基线（取法见 HostSettingBaseline）。
internal sealed class HostSettingDefaultsProvider(
    HostSettingBaseline baseline,
    IOptions<HostSettingBindingCollection> bindings) : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context)
    {
    }

    public void PostDefine(ISettingDefinitionContext context)
    {
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

            definition.DefaultValue = Normalize(definition, baseline.Lookup(binding))
                ?? Normalize(definition, binding.Fallback);
        }
    }

    // 归一到设置值的写法（配置里的 "warning"、"True" 也要对得上）；认不出时返回 null，由调用方用兜底值
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
