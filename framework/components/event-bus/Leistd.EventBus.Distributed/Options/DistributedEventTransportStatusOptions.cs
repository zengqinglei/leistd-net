namespace Leistd.EventBus.Distributed.Options;

/// <summary>传输健康状态上报与跨进程快照的新鲜度。</summary>
public sealed class DistributedEventTransportStatusOptions
{
    /// <summary>上报间隔，默认十五秒，须大于零且不超过原生计时器的最大间隔。</summary>
    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>上报过期时限，默认一分钟，须大于上报间隔。</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>实例标识；空值使用应用名、机器名和进程 ID，显式值须在并发实例间唯一。</summary>
    public string? Instance { get; set; }
}
