namespace CompanyName.ProjectName.Api.Options;

/// <summary>请求完成日志配置（配置节 <c>RequestLogging</c>）。</summary>
/// <remarks>
/// 宿主管理员可在系统设置的「运维」面板覆盖：宿主级设置经配置源覆盖本配置节，
/// 请求日志拦截器每个请求取 <c>IOptionsMonitor</c> 的当前值。
/// </remarks>
public sealed class RequestLoggingOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "RequestLogging";

    /// <summary>是否记录正常完成的请求；关闭后 5xx 仍记录。</summary>
    public bool Enabled { get; set; } = true;
}
