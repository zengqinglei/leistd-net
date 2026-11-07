namespace Leistd.EventBus.Distributed.Options;

/// <summary>传输接收与收件箱处理的独立分工。</summary>
public sealed class DistributedEventInboxOptions
{
    /// <summary>是否启动传输消费循环，默认开启。</summary>
    public bool ReceiveEnabled { get; set; } = true;

    /// <summary>是否立即处理并参与收件箱扫描，默认开启。</summary>
    public bool ProcessingEnabled { get; set; } = true;
}
