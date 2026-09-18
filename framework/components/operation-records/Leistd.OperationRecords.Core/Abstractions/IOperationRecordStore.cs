namespace Leistd.OperationRecords.Abstractions;

/// <summary>
/// 操作记录的持久化契约。
/// </summary>
/// <remarks>
/// 只负责写入与查询，不做动作码校验与租户判定：租户隔离由实现所在的数据过滤器承担，
/// 因此本契约不带租户参数。
/// <para><b>没有更新与删除。</b>审计记录一旦写下就不该再改——留了入口，"清理误记录"
/// 迟早会变成"清理不想被看到的记录"，而那时这张表已经不能作为证据了。
/// 保留策略属于运维范畴，用数据库分区或归档作业处理。</para>
/// </remarks>
public interface IOperationRecordStore
{
    /// <summary>写入一条记录。</summary>
    /// <remarks>
    /// <b>实现不得自行开启事务。</b>写入落在调用方所处的事务边界里：有环境工作单元就跟随它，
    /// 没有就即时生效。"这条记录要不要扛过外层回滚"是调用位置的问题，只有宿主清楚它的锁分布，
    /// 组件擅自开第二个事务会与外层未提交的写入互相加锁。
    /// </remarks>
    /// <param name="record">要写入的记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default);

    /// <summary>按创建时间倒序分页查询当前租户的记录。</summary>
    /// <remarks>
    /// <paramref name="startTime"/> 与 <paramref name="endTime"/> <b>按 UTC 比较</b>：
    /// 写入时记录的是 <c>IClock.Normalize</c> 归一后的 UTC 时刻，调用方传本地时刻会让整段区间偏移时区，
    /// 且偏移量随部署地而变——那种错查不出来，只会表现为"某些记录莫名不在范围里"。
    /// 界面上按展示时区选的日期，应由调用方换算成 UTC 再传入。
    /// </remarks>
    /// <param name="keyword">在动作码、目标标识与操作人名上做包含匹配；为空则不过滤。</param>
    /// <param name="startTime">起始时刻（含），UTC；为空则不设下界。</param>
    /// <param name="endTime">结束时刻（含），UTC；为空则不设上界。</param>
    /// <param name="skip">跳过条数。</param>
    /// <param name="take">取回条数。</param>
    /// <param name="scope">
    /// 可见范围，由调用方算好；默认不加限制，与旧行为一致。
    /// <b>本存储不判定"谁是宿主"</b>——那需要它不该有的上下文依赖，见
    /// <see cref="OperationRecordVisibilityScope"/>。
    /// </param>
    /// <param name="actions">
    /// 按动作码过滤，命中任一即匹配；<see langword="null"/> 或空集合表示不过滤。
    /// <para><b>没有"按类别过滤"的参数，这是刻意的。</b>类别定义在
    /// <see cref="IOperationActionDefinition"/> 上，而记录里只有动作码——
    /// 让存储去查定义管理器，等于给它加一个它不该有的依赖（本存储只注入 DbContext 提供器，
    /// 连当前用户都不认识）。<b>调用方把类别展开成动作码集合再传进来</b>，
    /// 与 <paramref name="scope"/> 的"调用方算好、存储只照做"是同一条边界。</para>
    /// </param>
    /// <param name="outcome">按结果过滤；<see langword="null"/> 表示不过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>总条数与当页记录。</returns>
    Task<OperationRecordPage> GetPagedListAsync(
        string? keyword,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take,
        OperationRecordVisibilityScope scope = default,
        IReadOnlyCollection<string>? actions = null,
        OperationRecordOutcome? outcome = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一页操作记录。
/// </summary>
/// <param name="TotalCount">满足条件的总条数。</param>
/// <param name="Items">当页记录，按创建时间倒序。</param>
public sealed record OperationRecordPage(long TotalCount, IReadOnlyList<OperationRecordInfo> Items);
