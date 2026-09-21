using Leistd.Data.Paging;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;

namespace Leistd.OperationRecords.Stores;

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
    /// <para>事务边界由记录的结果决定，这是契约的一部分，实现必须照做：</para>
    /// <list type="bullet">
    /// <item><see cref="OperationRecordOutcome.Succeeded"/>：落在调用方所处的事务边界里，
    /// 有环境工作单元就跟随它提交或回滚，没有就即时生效。记录的
    /// <see cref="OperationRecordInfo.TenantId"/> 必须与当前租户上下文一致。</item>
    /// <item><see cref="OperationRecordOutcome.Failed"/>：在 <see cref="OperationRecordInfo.TenantId"/>
    /// 所指的层里<b>独立写入并提交</b>，不随调用方回滚——失败记录描述的是一次没发生的变更，
    /// 没有可以同生共死的对象，而业务拒绝之后几乎总是紧跟着回滚。</item>
    /// </list>
    /// </remarks>
    /// <param name="record">要写入的记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default);

    /// <summary>按创建时间倒序分页查询当前租户的记录。</summary>
    /// <remarks>
    /// 筛选条件的语义见 <see cref="OperationRecordFilter"/>；分页只作用于取条目，总数按同一组筛选条件计算。
    /// <see cref="PageRequest.Sorting"/> 不生效：记录只有一种有意义的读法——按时间倒序看最近发生了什么。
    /// </remarks>
    /// <param name="filter">筛选条件。</param>
    /// <param name="page">分页。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>总条数与当页记录。</returns>
    Task<PagedResult<OperationRecordInfo>> GetPagedListAsync(
        OperationRecordFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 操作记录的存储层筛选条件。
/// </summary>
/// <remarks>
/// <para><b>时间按 UTC 比较</b>，两端都是闭区间：写入时记录的是 <c>IClock.Normalize</c> 归一后的 UTC 时刻，
/// 调用方传本地时刻会让整段区间偏移时区，且偏移量随部署地而变。界面上按展示时区选的日期，由调用方换算成 UTC。</para>
/// <para><b>没有"按类别过滤"。</b>类别定义在 <see cref="IOperationActionDefinition"/> 上，而记录里只有动作码；
/// 调用方把类别展开成动作码集合再传进来，与 <see cref="Scope"/> 的"调用方算好、存储只照做"是同一条边界。</para>
/// </remarks>
public sealed record OperationRecordFilter
{
    /// <summary>
    /// 可见范围，由调用方算好；必填，不过滤须显式传 <see cref="OperationRecordVisibilityScope.Unrestricted"/>。
    /// 本存储不判定"谁是宿主"——那需要它不该有的上下文依赖。
    /// </summary>
    public required OperationRecordVisibilityScope Scope { get; init; }

    /// <summary>在动作码、目标标识与操作人名上做包含匹配；为空不过滤。</summary>
    public string? Keyword { get; init; }

    /// <summary>起始时刻（含），UTC；为空不设下界。</summary>
    public DateTime? StartTime { get; init; }

    /// <summary>结束时刻（含），UTC；为空不设上界。</summary>
    public DateTime? EndTime { get; init; }

    /// <summary>按动作码过滤，命中任一即匹配；<see langword="null"/> 或空集合表示不过滤。</summary>
    public IReadOnlyCollection<string>? Actions { get; init; }

    /// <summary>按结果过滤；<see langword="null"/> 表示不过滤。</summary>
    public OperationRecordOutcome? Outcome { get; init; }
}
