namespace Leistd.ServiceClient.Options;

/// <summary>服务客户端配置基类；每个下游服务派生一个具体类型，绑定 <c>Leistd:ServiceClients:&lt;服务名&gt;</c>。</summary>
public class ServiceClientOptions
{
    /// <summary>下游服务基础地址（如 <c>http://order-service</c>），结尾自动补 <c>/</c>。</summary>
    /// <remarks>
    /// 可选且不做启动期校验：宿主可在返回的 <c>IHttpClientBuilder</c> 上自行设置。都未设置时，调用在发出请求时失败。
    /// </remarks>
    public string? BaseAddress { get; set; }

    /// <summary>是否向该服务转发关联标识请求头；默认 <see langword="true"/>。</summary>
    /// <remarks>
    /// <para>调用不接收本系统内部关联标识的第三方时设为 <see langword="false"/>，该客户端不挂关联标识处理器，其他客户端不受影响。
    /// 经 <c>IOptions</c> 在进程内首次构建处理器链时读取一次，之后固定，配置重载不生效。</para>
    /// <para>只控制 Leistd.Tracing 的关联标识头；W3C <c>traceparent</c> 由 .NET 运行时传播，需要关闭时用官方
    /// <c>DistributedContextPropagator</c>。宿主未注册关联标识时本项不起作用。</para>
    /// </remarks>
    public bool ForwardCorrelationId { get; set; } = true;
}
