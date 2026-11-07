namespace Leistd.EventBus.Distributed.Transport;

/// <summary>接收与拒收存储使用的原始消息；传输须复制 SDK 提供的短生命周期缓冲区。</summary>
public sealed record RawDistributedEvent
{
    /// <summary>原始消息字节，拒收时原样持久化。</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>线上消息头，缺失时为空字典。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>不透明坐标，例如主题、分区、位点或外部事件标识。</summary>
    public required IReadOnlyDictionary<string, string> Coordinates { get; init; }

    /// <summary>首次收到消息的时刻（UTC）。</summary>
    public required DateTime ReceivedAt { get; init; }
}
