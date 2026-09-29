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
    /// <remarks>
    /// 有意可选、也有意不做启动期校验：基地址可以由宿主在返回的 <c>IHttpClientBuilder</c> 上
    /// 自行设置（服务发现、测试替身）。这里留空且别处也没设时，调用在发出请求时失败。
    /// </remarks>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// 用户上下文出站转发配置。
    /// </summary>
    public UserContextForwardingOptions UserContext { get; set; } = new();
}
