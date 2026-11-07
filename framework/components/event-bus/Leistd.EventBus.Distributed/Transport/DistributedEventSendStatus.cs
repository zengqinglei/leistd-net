namespace Leistd.EventBus.Distributed.Transport;

/// <summary>发送确认结果；与具体 broker 的确认模型无关。</summary>
public enum DistributedEventSendStatus
{
    /// <summary>已获得传输确认。</summary>
    Succeeded,
    /// <summary>暂时不可用或结果未知，可以原样重试。</summary>
    TransientFailure,
    /// <summary>单条消息不可恢复，立即进入死信。</summary>
    PermanentFailure
}
