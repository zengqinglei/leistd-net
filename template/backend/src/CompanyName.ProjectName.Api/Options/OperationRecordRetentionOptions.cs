using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Api.Options;

/// <summary>
/// 操作记录保留期配置（配置节 <c>OperationRecordRetention</c>）。
/// </summary>
/// <remarks>
/// <para><b>默认关闭。</b>审计表只增不减是安全的默认值；一个默认就会删审计数据的开关，
/// 会让不知情的部署在某天夜里悄悄丢掉合规所需的历史。要启用就得有人显式打开，
/// 那一刻他也就为保留期负了责。</para>
/// <para><see cref="Enabled"/> 与 <see cref="RetentionDays"/> 可由宿主管理员在系统设置的「审计」面板覆盖：
/// 宿主级设置经配置源覆盖本配置节的对应键，归档任务每轮取 <c>IOptionsMonitor</c> 的当前值。
/// 执行时刻与批大小是部署调优参数，只在配置里。</para>
/// <para><b>到期记录搬去归档表，不是删除。</b>
/// <c>IOperationRecordStore</c> 的契约明写「没有更新与删除……保留策略属于运维范畴，
/// 用数据库分区或归档作业处理」——留了删除入口，「清理误记录」迟早会变成
/// 「清理不想被看到的记录」，而那时这张表已经不能作为证据了。搬走的数据仍在库里，
/// 只是不再参与日常查询。</para>
/// </remarks>
public class OperationRecordRetentionOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "OperationRecordRetention";

    /// <summary>保留天数的下限。</summary>
    /// <remarks>
    /// 低于这个值基本上等同于「随手清掉最近发生的事」，而最近发生的事正是审计最常被追问的部分。
    /// </remarks>
    public const int MinimumRetentionDays = 30;

    /// <summary>是否启用到期归档。</summary>
    public bool Enabled { get; set; }

    /// <summary>保留天数：早于「当前时刻减去该天数」的记录会被搬入归档表。</summary>
    [Range(MinimumRetentionDays, 3650, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int RetentionDays { get; set; } = 365;

    /// <summary>每天执行归档的时刻（UTC 小时，0–23）。</summary>
    /// <remarks>
    /// 按 UTC 而非本地时区：部署地会变，而「凌晨跑」这件事的意义来自业务低谷，
    /// 不来自服务器碰巧设了哪个时区。需要对齐当地凌晨时由部署侧换算后填入。
    /// </remarks>
    [Range(0, 23, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int DailyRunHourUtc { get; set; } = 18;

    /// <summary>单批搬运的条数。</summary>
    /// <remarks>
    /// <b>分批不是优化，是必需</b>：一次性把几十万行读进内存再保存，会让这个后台任务
    /// 成为进程里最大的一次内存峰值，而它本该是最不起眼的。
    /// </remarks>
    [Range(100, 10000, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int BatchSize { get; set; } = 500;
}
