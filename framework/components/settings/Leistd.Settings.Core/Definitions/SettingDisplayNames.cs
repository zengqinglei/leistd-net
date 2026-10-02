using Leistd.Settings.Options;
using Microsoft.Extensions.Localization;

namespace Leistd.Settings.Definitions;

// 设置的显示名只有这一处取法：宿主资源里的 Setting:{Name} → 定义上的 DisplayName → Name。
// 设置列表与写入校验的错误提示共用它；两处各写一份时，界面显示「锁定时长（分钟）」，
// 同一个字段的报错却是「Security.LockoutDurationMinutes」。
// 定义是启动时加载的单例，翻译必须发生在请求阶段，否则先到的请求的语言会被固化给所有人。
internal static class SettingDisplayNames
{
    public static IStringLocalizer? CreateLocalizer(
        IStringLocalizerFactory? localizerFactory,
        SettingManagementOptions? options)
        => options?.LocalizationResource is { } resource ? localizerFactory?.Create(resource) : null;

    public static string Resolve(ISettingDefinition definition, IStringLocalizer? localizer)
        => Localize(localizer, "Setting:" + definition.Name)
            ?? (string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Name : definition.DisplayName);

    public static string? Localize(IStringLocalizer? localizer, string key)
    {
        if (localizer is null)
            return null;

        var localized = localizer[key];
        return localized.ResourceNotFound ? null : localized.Value;
    }
}
