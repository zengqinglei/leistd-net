namespace CompanyName.ProjectName.Application.Settings.Options;

/// <summary>遥测导出的设置项是否登记，由 Api 组合根按部署配置设置。</summary>
/// <remarks>
/// 部署整体关闭 OpenTelemetry SDK（<c>OTEL_SDK_DISABLED=true</c>）时没有导出管道，SDK 也只在启动时读这个开关：
/// 设置页上的导出开关改了不会生效，因此不登记。
/// </remarks>
public sealed class TelemetrySettingsOptions
{
    /// <summary>是否登记 <c>Telemetry.Export.*</c> 三项设置。</summary>
    public bool ExportConfigurable { get; set; } = true;
}
