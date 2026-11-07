namespace Leistd.EventBus.Distributed.Transport;

/// <summary>每个服务选择一个传输；实现必须线程安全，不捕获接收工作单元的作用域。</summary>
public interface IDistributedEventTransport
{
    /// <summary>管理与拒收记录展示的稳定传输名称。</summary>
    string Name { get; }

    /// <summary>发送并等待确认；通道故障返回瞬态失败，单条不可恢复问题返回永久失败。取消向上传播。</summary>
    Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken);

    /// <summary>异步映射原始消息；不可恢复格式错误抛解码异常，暂时性依赖故障与取消向上传播。</summary>
    ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken);
}
