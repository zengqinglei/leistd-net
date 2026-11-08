namespace Leistd.EventBus.Distributed.Options;

/// <summary>分布式事件的部署配置。</summary>
public sealed class DistributedEventOptions
{
    /// <summary>默认配置节。</summary>
    public const string SectionName = "Leistd:EventBus:Distributed";

    /// <summary>指数退避起点，默认两秒。</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>退避上限，默认五分钟。</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>从首次失败算起的发送重试窗口，默认一天。</summary>
    public TimeSpan DeadLetterAfter { get; set; } = TimeSpan.FromDays(1);

    /// <summary>每轮收件箱处理次数，默认五次；重放重新计数。</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>发件箱与收件箱扫描间隔，默认五秒。</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>最旧待发记录的告警阈值，默认五分钟。</summary>
    public TimeSpan BacklogWarningAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>发送进程分工。</summary>
    public DistributedEventOutboxOptions Outbox { get; set; } = new();

    /// <summary>接收与处理进程分工。</summary>
    public DistributedEventInboxOptions Inbox { get; set; } = new();

    /// <summary>内置测试事件。</summary>
    public DistributedEventTestOptions TestEvent { get; set; } = new();

    /// <summary>传输健康状态上报与跨进程快照。</summary>
    public DistributedEventTransportStatusOptions TransportStatus { get; set; } = new();

}
