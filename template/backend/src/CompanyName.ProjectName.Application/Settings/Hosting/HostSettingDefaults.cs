namespace CompanyName.ProjectName.Application.Settings.Hosting;

/// <summary>
/// 覆盖部署配置的宿主级设置在"没有设置时"的值，即部署基线，用作这些设置的代码默认值。
/// </summary>
/// <remarks>
/// <para>这些设置经一个优先级最高的配置源覆盖对应的配置键，消费方照常注入 <c>IOptionsMonitor&lt;T&gt;</c>。
/// 默认值因此不能再从 <c>IConfiguration</c> 现取：设置加载进配置之后，读到的就是覆盖后的值，
/// 界面上的默认值随之变成当前值，「重置」也就回不到部署基线。</para>
/// <para>由宿主取得（见 Api 层的 <c>HostSettingBindings.CaptureDefaults</c>，取值时跳过宿主设置配置源本身）。
/// 没有宿主提供时（如迁移程序）为空，这些设置的默认值为 <see langword="null"/>——那里本来也不读它们。</para>
/// </remarks>
/// <param name="values">设置名 → 部署基线值。</param>
public sealed class HostSettingDefaults(IReadOnlyDictionary<string, string?> values)
{
    /// <summary>没有宿主提供基线时的空实例。</summary>
    public static HostSettingDefaults None { get; } = new(new Dictionary<string, string?>());

    /// <summary>某个设置的部署基线值；不是经配置源覆盖的设置时为 <see langword="null"/>。</summary>
    /// <param name="settingName">设置名。</param>
    public string? Get(string settingName) => values.GetValueOrDefault(settingName);
}
