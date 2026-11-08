namespace Leistd.EventBus.Distributed.Options;

/// <summary>发送分工；关闭后发布仍写发件箱。</summary>
public sealed class DistributedEventOutboxOptions
{
    /// <summary>是否提交后立即发送并参与发件箱扫描，默认开启。</summary>
    public bool DispatchEnabled { get; set; } = true;
}
