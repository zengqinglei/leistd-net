namespace Leistd.Webhooks.EntityFrameworkCore.Options;

/// <summary>webhook 记录保留期。配置节 <c>Leistd:Webhooks:Retention</c>。</summary>
/// <remarks>默认开启：事件、投递与尝试记录属于运营数据，到期一并删除。</remarks>
public sealed class WebhookRetentionOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Leistd:Webhooks:Retention";

    /// <summary>是否启用到期清理，默认开启；关闭时记录永久保留，统计的保留期下限为空。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>保留天数（1–3650），按投递创建时间计，默认 62：覆盖 30 天视图及其上一区间（含时区边界）。</summary>
    public int Days { get; set; } = 62;

    /// <summary>每天执行的 UTC 小时（0–23），默认 19。</summary>
    public int DailyRunHourUtc { get; set; } = 19;

    /// <summary>单批删除的投递数（100–10000），默认 500；每批连同其尝试记录一个事务。</summary>
    public int BatchSize { get; set; } = 500;
}
