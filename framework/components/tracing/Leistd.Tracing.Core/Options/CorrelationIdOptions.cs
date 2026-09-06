namespace Leistd.Tracing.Options;

/// <summary>
/// 链路追踪配置。配置节 <c>Leistd:CorrelationId</c>。
/// </summary>
public class CorrelationIdOptions
{
    /// <summary>
    /// 是否启用链路标识处理。默认 <see langword="true"/>。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 用于读取和传播链路标识的 HTTP Header 名称。
    /// </summary>
    public string[] HeaderNames { get; set; } = ["X-Correlation-Id"];

    /// <summary>
    /// 是否将链路标识写入响应头。默认 <see langword="true"/>。
    /// </summary>
    public bool IncludeInResponseHeaders { get; set; } = true;
}
