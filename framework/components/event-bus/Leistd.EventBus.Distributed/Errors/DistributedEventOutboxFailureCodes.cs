namespace Leistd.EventBus.Distributed.Errors;

/// <summary>框架写入发件箱的失败码；传输自己的失败码由传输定义。</summary>
public static class DistributedEventOutboxFailureCodes
{
    /// <summary>传输或本服务内事件的本地投递抛出未分类异常，按瞬态失败重试。</summary>
    public const string TransportException = "transport-exception";

    /// <summary>从首次失败算起已超过发送重试窗口。</summary>
    public const string RetryWindowExceeded = "retry-window-exceeded";

    /// <summary>派发实例没有登记该本服务内事件的任何消费者；补齐登记后可重投。</summary>
    public const string InternalNoConsumer = "internal-no-consumer";

    /// <summary>本服务内事件的载荷无法还原为消费者的事件类型。</summary>
    public const string InternalInvalidPayload = "internal-invalid-payload";

    /// <summary>租户目录明确返回本服务内事件所属的租户不存在。</summary>
    public const string InternalUnknownTenant = "internal-unknown-tenant";
}
