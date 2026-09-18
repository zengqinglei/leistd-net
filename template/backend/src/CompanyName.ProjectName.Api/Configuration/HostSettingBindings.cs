using System.Globalization;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Hosting;
using CompanyName.ProjectName.Application.Settings.Provider;
#if (LocalIdentity)
using Leistd.Email.Smtp.Options;
#endif

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>
/// 哪些宿主级设置覆盖哪些配置键。
/// </summary>
/// <remarks>
/// 新增一项运行期可改的部署配置只需三步：定义宿主级设置、在 <see cref="All"/> 里加一行、
/// 消费方注入 <c>IOptionsMonitor&lt;T&gt;</c>（或 <c>IOptionsSnapshot&lt;T&gt;</c>）。
/// 属性名用 <c>nameof</c> 取，改名时编译期就能发现映射断了。
/// </remarks>
public static class HostSettingBindings
{
    /// <summary>Serilog 最小级别的配置节，见 <see cref="All"/> 里的说明。</summary>
    public const string SerilogMinimumLevel = "Serilog:MinimumLevel";

    /// <summary>全部映射。</summary>
    public static IReadOnlyList<HostSettingBinding> All { get; } =
    [
        // Serilog 的最小级别有标量与对象两种写法，它在启动时按配置源优先级选定一种并订阅其重载。
        // 两个键都写：无论它订阅的是哪一种，拿到的都是设置里的值；清除设置即回到部署配置。
        // 前提是部署配置里至少有一种写法（模板的 appsettings.json 自带），否则 Serilog 没有可订阅的键。
        new(SettingConstant.Logging.MinimumLevel,
            [SerilogMinimumLevel, $"{SerilogMinimumLevel}:Default"],
            Fallback: nameof(Serilog.Events.LogEventLevel.Information),
            Normalize: NormalizeLevel),
        Option<RequestLoggingOptions>(SettingConstant.Logging.RequestLevel,
            RequestLoggingOptions.SectionName, nameof(RequestLoggingOptions.Level)) with { Normalize = NormalizeLevel },
#if (LocalIdentity)
        Option<SmtpOptions>(SettingConstant.Email.SmtpHost, SmtpOptions.SectionName, nameof(SmtpOptions.Host)),
        Option<SmtpOptions>(SettingConstant.Email.SmtpPort, SmtpOptions.SectionName, nameof(SmtpOptions.Port)),
        Option<SmtpOptions>(SettingConstant.Email.SmtpEnableSsl, SmtpOptions.SectionName, nameof(SmtpOptions.EnableSsl)),
        Option<SmtpOptions>(SettingConstant.Email.SmtpUsername, SmtpOptions.SectionName, nameof(SmtpOptions.Username)),
        Option<SmtpOptions>(SettingConstant.Email.SmtpPassword, SmtpOptions.SectionName, nameof(SmtpOptions.Password)) with { IsSecret = true },
        Option<SmtpOptions>(SettingConstant.Email.DefaultFromAddress, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromAddress)),
        Option<SmtpOptions>(SettingConstant.Email.DefaultFromName, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromName)),
#endif
        Option<OperationRecordRetentionOptions>(SettingConstant.Audit.RetentionEnabled,
            OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.Enabled)),
        Option<OperationRecordRetentionOptions>(SettingConstant.Audit.RetentionDays,
            OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.RetentionDays)),
    ];

    /// <summary>
    /// 取这些设置的部署基线（设置的代码默认值），见 <see cref="HostSettingDefaults"/>。
    /// </summary>
    /// <remarks>
    /// <para>逐个配置提供程序查值并<b>跳过宿主设置那一个</b>：无论设置是否已加载进配置，拿到的都是部署配置里的值，
    /// 不依赖调用时机。取值规则与 <c>IConfiguration</c> 一致——后加入的配置源优先；一个设置有多个键时，
    /// 同一配置源内按键的顺序取。哪个配置源都没有时用映射给出的兜底值（Options 类型上的属性默认值）。</para>
    /// <para>机密设置不给默认值，口令不该作为默认值下发到界面。</para>
    /// </remarks>
    /// <param name="configuration">应用配置。</param>
    public static HostSettingDefaults CaptureDefaults(IConfigurationRoot configuration)
    {
        var deployment = configuration.Providers.Where(provider => provider is not HostSettingsConfigurationProvider).Reverse().ToList();

        return new HostSettingDefaults(All.ToDictionary(
            binding => binding.SettingName,
            binding => binding.IsSecret ? null : binding.Normalize(Lookup(deployment, binding) ?? binding.Fallback),
            StringComparer.Ordinal));
    }

    private static string? Lookup(IReadOnlyList<IConfigurationProvider> deployment, HostSettingBinding binding)
    {
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

    // 绑定到某个 Options 属性：配置键是"节:属性名"，兜底值取该类型上的属性默认值；布尔值归一成小写，与设置值同一写法
    private static HostSettingBinding Option<TOptions>(string settingName, string section, string property)
        where TOptions : new()
    {
        var info = typeof(TOptions).GetProperty(property)
            ?? throw new InvalidOperationException($"{typeof(TOptions).Name} has no property '{property}'.");
        return new HostSettingBinding(
            settingName,
            [$"{section}:{property}"],
            Format(info.GetValue(new TOptions())),
            Normalize: info.PropertyType == typeof(bool) ? NormalizeBoolean : static value => value);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        string text => string.IsNullOrEmpty(text) ? null : text,
        _ => value.ToString(),
    };

    private static string? NormalizeBoolean(string? value) =>
        bool.TryParse(value, out var flag) ? (flag ? "true" : "false") : value;

    // 归一到候选项里那份写法：设置值按 Ordinal 比对，配置里写成 "warning" 也要能对上；
    // 认不出的取值退回 Information，而不是原样下发一个连写入端都不接受的默认值
    private static string? NormalizeLevel(string? value) =>
        SettingConstant.Logging.Levels.FirstOrDefault(level => string.Equals(level, value, StringComparison.OrdinalIgnoreCase))
        ?? nameof(Serilog.Events.LogEventLevel.Information);
}

/// <summary>一个宿主级设置与它覆盖的配置键。</summary>
/// <param name="SettingName">设置名。</param>
/// <param name="ConfigurationKeys">被覆盖的配置键；设置有值时每个键都写成这个值。</param>
/// <param name="Fallback">哪个配置源都没有这些键时的部署基线。</param>
/// <param name="Normalize">把配置里的写法归一成设置值的写法（用于部署基线）。</param>
/// <param name="IsSecret">是否机密：不作为默认值下发。</param>
public sealed record HostSettingBinding(
    string SettingName,
    IReadOnlyList<string> ConfigurationKeys,
    string? Fallback,
    Func<string?, string?> Normalize,
    bool IsSecret = false);
