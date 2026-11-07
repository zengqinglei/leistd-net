namespace Leistd.EventBus.Distributed.Options;

/// <summary>仅删除最后变更已过期的终态记录；Pending 永不清理。</summary>
public sealed class DistributedEventRetentionOptions
{
    /// <summary>默认配置节。</summary>
    public const string SectionName = "Leistd:EventBus:Distributed:Retention";

    /// <summary>是否启用每日保留期任务，默认开启。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>终态保留天数，默认十四天；也是收件箱去重窗口。</summary>
    public int Days { get; set; } = 14;
}
