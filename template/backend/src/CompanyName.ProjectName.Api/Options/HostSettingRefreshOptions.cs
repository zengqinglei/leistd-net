namespace CompanyName.ProjectName.Api.Options;

/// <summary>
/// 宿主级设置的刷新周期
/// </summary>
/// <remarks>
/// 决定"在别的实例上改的设置多久生效"。留在部署配置里而不是做成设置项——
/// 见 <see cref="HostSettingRefreshJob"/> 的说明。
/// </remarks>
public sealed class HostSettingRefreshOptions
{
    /// <summary>配置段名。</summary>
    public const string SectionName = "HostSettings";

    /// <summary>刷新周期；默认 30 秒。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}
