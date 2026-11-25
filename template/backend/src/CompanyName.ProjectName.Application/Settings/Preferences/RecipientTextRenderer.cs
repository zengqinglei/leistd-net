using System.Globalization;
#if (IncludeLocalization)
using CompanyName.ProjectName.Application.Settings.Provider;
#endif
using CompanyName.ProjectName.Application.Settings.Timing;

namespace CompanyName.ProjectName.Application.Settings.Preferences;

/// <summary>按收件人的生效语言和时区生成待投递或保存的文本。</summary>
/// <remarks>调用方先建立收件人的租户上下文。区域性只在同步渲染期间有效。</remarks>
/// <param name="preferenceStore">按收件人读取生效账户偏好。</param>
public sealed class RecipientTextRenderer(IAccountPreferenceStore preferenceStore)
{
    /// <summary>读取一次所需偏好，在收件人区域性下渲染，随后恢复调用方的区域性。</summary>
    /// <param name="userId">收件人的用户 Id。</param>
    /// <param name="render">同步渲染。需要显示时刻时，按传入的时区换算后格式化。</param>
    /// <param name="useTimeZone">正文需要显示收件人当地的时刻。否则给委托传 UTC，单语言形态无需读取偏好。</param>
    /// <param name="cancellationToken">偏好读取的取消令牌。</param>
    public async Task<TResult> RenderAsync<TResult>(
        Guid userId,
        Func<TimeZoneInfo, TResult> render,
        bool useTimeZone = false,
        CancellationToken cancellationToken = default)
    {
#if (IncludeLocalization)
        var preferences = (await preferenceStore.GetForUserAsync(userId, cancellationToken))?.Preferences;
        var supported = SettingConstant.Display.SupportedLanguages;
        var language = preferences?.Language;
        var culture = CultureInfo.GetCultureInfo(language is not null && supported.Contains(language, StringComparer.Ordinal)
            ? language
            : supported[0]);
#else
        var preferences = useTimeZone ? (await preferenceStore.GetForUserAsync(userId, cancellationToken))?.Preferences : null;
        var culture = CultureInfo.GetCultureInfo("en");
#endif
        var timeZone = useTimeZone && UserTimeZoneProvider.TryResolveId(preferences?.TimeZone, out var resolved)
            ? resolved
            : TimeZoneInfo.Utc;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return render(timeZone);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
