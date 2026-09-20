namespace Leistd.OperationRecords.EntityFrameworkCore.Options;

/// <summary>
/// 操作记录保留期。配置节 <c>Leistd:OperationRecords:Retention</c>。
/// </summary>
/// <remarks>
/// <para><b>默认关闭。</b>审计表只增不减是安全的默认值；保留期受法律与合同约束，组件无从知道，
/// 要启用就得有人显式打开并为保留期负责。</para>
/// <para><b>到期记录搬去归档表，不是删除。</b>存储契约没有删除入口；搬走的数据仍在库里，只是不再参与日常查询。</para>
/// <para>归档任务每轮取 <c>IOptionsMonitor</c> 的当前值，<see cref="Enabled"/> 与 <see cref="RetentionDays"/> 改完下一轮生效；
/// 执行时刻只在任务排期时取一次。</para>
/// </remarks>
public sealed class OperationRecordRetentionOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Leistd:OperationRecords:Retention";

    /// <summary>保留天数下限：低于它基本等同于"随手清掉最近发生的事"，而那正是审计最常被追问的部分。</summary>
    public const int MinimumRetentionDays = 30;

    /// <summary>保留天数上限。</summary>
    public const int MaximumRetentionDays = 3650;

    /// <summary>是否启用到期归档，默认关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>保留天数：早于"当前时刻减去该天数"的记录搬入归档表，取值见上下限常量，默认 365。</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>每天执行的 UTC 小时（0–23），默认 18；按 UTC 而非服务器时区，需要对齐当地凌晨时由部署侧换算。</summary>
    public int DailyRunHourUtc { get; set; } = 18;

    /// <summary>单批搬运的条数（100–10000），默认 500；每批一个事务，分批是为了控制事务与内存的上限。</summary>
    public int BatchSize { get; set; } = 500;
}
