namespace Leistd.EventBus.Distributed.Errors;

/// <summary>框架写入发件箱的失败码；传输自己的失败码由传输定义。</summary>
public static class DistributedEventOutboxFailureCodes
{
    /// <summary>传输抛出未分类异常，按瞬态失败重试。</summary>
    public const string TransportException = "transport-exception";

    /// <summary>从首次失败算起已超过发送重试窗口。</summary>
    public const string RetryWindowExceeded = "retry-window-exceeded";
}
