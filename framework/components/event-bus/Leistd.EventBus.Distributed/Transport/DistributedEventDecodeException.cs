namespace Leistd.EventBus.Distributed.Transport;

/// <summary>单条消息无法解码；不能用它包装暂时性连接或注册中心故障。</summary>
public sealed class DistributedEventDecodeException : Exception
{
    /// <summary>稳定拒收原因码。</summary>
    public string ReasonCode { get; }

    /// <summary>指定拒收原因码与诊断文本。</summary>
    public DistributedEventDecodeException(string reasonCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ReasonCode = reasonCode;
    }
}
