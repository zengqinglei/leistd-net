namespace Leistd.Settings.Hosting.Options;

/// <summary>
/// 宿主级设置的刷新周期。
/// </summary>
/// <remarks>
/// 决定"在别的实例上改的设置多久在本实例生效"。留在部署配置里而不是做成设置项：
/// 它自己做成设置就成了自举依赖——改错了还得靠它自己来纠正。
/// </remarks>
public sealed class HostSettingOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Leistd:Settings:Hosting";

    /// <summary>刷新周期，不小于 1 秒；默认 30 秒。</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
