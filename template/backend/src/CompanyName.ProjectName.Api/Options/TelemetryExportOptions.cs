namespace CompanyName.ProjectName.Api.Options;

/// <summary>OTLP 导出的当前有效值：是否发送、发到哪个基地址、带哪些认证头。</summary>
/// <remarks>
/// <para>三项都可在系统设置覆盖（见 <c>HostSettingBindings</c>）。地址与认证头直接读 OpenTelemetry 原本使用的
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>、<c>OTEL_EXPORTER_OTLP_HEADERS</c>，部署值即兜底；
/// 开关读 <c>Telemetry:Export:Enabled</c>，缺省时取 <see cref="IsEnabledByDeployment"/> 的部署基线。</para>
/// <para>导出管道常驻，按 <see cref="IsActive"/> 在三处决定是否继续：追踪的采样器、日志进入批处理之前、发送层。</para>
/// </remarks>
public sealed class TelemetryExportOptions
{
    /// <summary>导出开关的配置键。</summary>
    public const string EnabledKey = "Telemetry:Export:Enabled";

    /// <summary>导出基地址的配置键（OpenTelemetry 标准环境变量）。</summary>
    public const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>导出认证头的配置键（OpenTelemetry 标准环境变量，格式 <c>key1=value1,key2=value2</c>）。</summary>
    public const string HeadersKey = "OTEL_EXPORTER_OTLP_HEADERS";

    /// <summary>整体关闭 OpenTelemetry SDK 的配置键（OpenTelemetry 标准环境变量）。</summary>
    public const string SdkDisabledKey = "OTEL_SDK_DISABLED";

    /// <summary>是否发送遥测。</summary>
    public bool Enabled { get; set; }

    /// <summary>OTLP/HTTP 基地址；各信号在其后追加 <c>/v1/{signal}</c>。</summary>
    public string? Endpoint { get; set; }

    /// <summary>认证头，格式同 <c>OTEL_EXPORTER_OTLP_HEADERS</c>。</summary>
    public string? Headers { get; set; }

    /// <summary>此刻是否导出：开关打开且有地址。</summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>开关的部署基线：部署配置里的导出地址非空白即启用。</summary>
    /// <remarks>
    /// 只在注册期对部署配置求值：设置绑定的兜底值与选项缺键时的取值都来自它，两处一致。
    /// 运行期的有效配置已含系统设置的地址覆盖，不能拿来重算基线。
    /// </remarks>
    public static bool IsEnabledByDeployment(IConfiguration deploymentConfiguration)
        => !string.IsNullOrWhiteSpace(deploymentConfiguration[EndpointKey]);

    /// <summary>部署是否整体关闭了 OpenTelemetry SDK（<c>OTEL_SDK_DISABLED=true</c>，与 SDK 一样只认 <c>true</c>）。</summary>
    /// <remarks>
    /// SDK 只在宿主构建时读这个开关。关闭时不注册采集与导出，系统设置里也没有遥测导出项：运行期打开导出不会生效。
    /// </remarks>
    public static bool IsSdkDisabled(IConfiguration deploymentConfiguration)
        => bool.TryParse(deploymentConfiguration[SdkDisabledKey], out var disabled) && disabled;
}
