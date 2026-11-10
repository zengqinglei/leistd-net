namespace CompanyName.ProjectName.Api.Options;

/// <summary>OTLP 导出的当前有效值：是否发送、发到哪个基地址、带哪些认证头。</summary>
/// <remarks>
/// <para>三项都可在系统设置覆盖（见 <c>HostSettingBindings</c>）。地址与认证头直接读 OpenTelemetry 原本使用的
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>、<c>OTEL_EXPORTER_OTLP_HEADERS</c>，部署值即兜底；
/// 开关读 <c>Telemetry:Export:Enabled</c>，缺省时取 <see cref="IsEnabledByDeployment"/> 的部署基线。</para>
/// <para>只由发送层（<c>TelemetryExportHandler</c>）消费：导出器常驻，每次发送按当时的值决定发不发、发到哪、带什么头。</para>
/// </remarks>
public sealed class TelemetryExportOptions
{
    /// <summary>导出开关的配置键。</summary>
    public const string EnabledKey = "Telemetry:Export:Enabled";

    /// <summary>导出基地址的配置键（OpenTelemetry 标准环境变量）。</summary>
    public const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>导出认证头的配置键（OpenTelemetry 标准环境变量，格式 <c>key1=value1,key2=value2</c>）。</summary>
    public const string HeadersKey = "OTEL_EXPORTER_OTLP_HEADERS";

    /// <summary>是否发送遥测。</summary>
    public bool Enabled { get; set; }

    /// <summary>OTLP/HTTP 基地址；各信号在其后追加 <c>/v1/{signal}</c>。</summary>
    public string? Endpoint { get; set; }

    /// <summary>认证头，格式同 <c>OTEL_EXPORTER_OTLP_HEADERS</c>。</summary>
    public string? Headers { get; set; }

    /// <summary>开关的部署基线：部署配置里的导出地址非空白即启用。</summary>
    /// <remarks>
    /// 只在注册期对部署配置求值：设置绑定的兜底值与选项缺键时的取值都来自它，两处一致。
    /// 运行期的有效配置已含系统设置的地址覆盖，不能拿来重算基线。
    /// </remarks>
    public static bool IsEnabledByDeployment(IConfiguration deploymentConfiguration)
        => !string.IsNullOrWhiteSpace(deploymentConfiguration[EndpointKey]);
}
