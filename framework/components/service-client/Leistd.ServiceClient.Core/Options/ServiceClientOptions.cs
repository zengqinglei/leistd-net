namespace Leistd.ServiceClient.Options;

/// <summary>
/// 服务客户端配置基类。业务 Client 包为每个下游服务派生一个具体 Options 类型，
/// 绑定配置节 <c>Leistd:ServiceClients:&lt;服务名&gt;</c>。
/// </summary>
public class ServiceClientOptions
{
    /// <summary>
    /// 下游服务基础地址（如 <c>http://order-service</c>）。结尾自动补 <c>/</c>。
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// 单次调用超时。默认 30 秒。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否在 Debug 级别记录请求/响应载荷（含脱敏后的头）。默认 <c>false</c>。
    /// 开启后响应体会被完整缓冲，勿用于文件流等大响应客户端。
    /// </summary>
    public bool LogPayloads { get; set; }

    /// <summary>
    /// 载荷日志的最大长度（字符），超出截断。默认 4096。
    /// </summary>
    public int MaxPayloadLength { get; set; } = 4096;

    /// <summary>
    /// 用户上下文出站转发配置。
    /// </summary>
    public UserContextForwardingOptions UserContext { get; set; } = new();
}
