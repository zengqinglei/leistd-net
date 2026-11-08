namespace Leistd.EventBus.Distributed.Transport;

/// <summary>传输调用的持久接收边界；每条消息创建作用域后解析本服务。</summary>
public interface IDistributedEventReceiver
{
    /// <summary>解码并持久写入收件箱或拒收表，成功返回后传输才能确认；依赖故障与取消向上传播。</summary>
    Task ReceiveAsync(RawDistributedEvent raw, CancellationToken cancellationToken);

    /// <summary>传输已判定为坏消息时直接写宿主拒收表；失败记日志后抛出，传输能重投时必须重投。</summary>
    Task RejectAsync(RawDistributedEvent raw, string reasonCode, string error, CancellationToken cancellationToken);
}
