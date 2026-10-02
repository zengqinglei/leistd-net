using Leistd.OperationRecords.Stores;

namespace Leistd.OperationRecords.Models;

/// <summary>
/// 查询时的可见范围：<b>调用方算好，存储只照做</b>。
/// </summary>
/// <remarks>
/// <para><b>刻意不让存储自己判定"谁是宿主"。</b><see cref="IOperationRecordStore"/> 的契约是
/// "只负责写入与查询，不做动作码校验与租户判定"，它连当前用户都不注入。若把"读者是不是宿主"
/// 这类判断挪进组件，等于让存储层长出它不该有的上下文依赖，而宿主对"谁算宿主"的定义
/// 只有宿主自己知道。因此这里只承载两个已经算好的事实。</para>
/// <para><b>租户维度不在此列。</b>跨租户隔离由 <c>IMultiTenant</c> 的全局查询过滤器承担
/// （谓词是 <c>TenantId == CurrentTenantId</c>，宿主视角下即 <c>TenantId == null</c>），
/// 本类型只在同一层内部再分一次"这条给不给看"。</para>
/// <para><b>可见性是安全边界，没有默认值。</b>实例只能从 <see cref="Host"/>、
/// <see cref="ForTenantReader"/> 与 <see cref="Unrestricted"/> 取得：做成值类型的话，
/// <c>default</c> 总得落在某个取值上，落在"不过滤"就是漏记一个参数即越权，
/// 落在"最严"又会让宿主查出空结果。不过滤只能显式写出来。</para>
/// </remarks>
public sealed class OperationRecordVisibilityScope
{
    private OperationRecordVisibilityScope(bool restricted, bool includesHostRecords, string? actorId, Guid? actorTenantId)
    {
        IsRestricted = restricted;
        IncludesHostRecords = includesHostRecords;
        ActorId = actorId;
        ActorTenantId = actorTenantId;
    }

    /// <summary>是否施加可见性限制；仅 <see cref="Unrestricted"/> 为 <see langword="false"/>。</summary>
    public bool IsRestricted { get; }

    /// <summary>读者能否看到 <see cref="OperationVisibility.Host"/> 层的记录。</summary>
    public bool IncludesHostRecords { get; }

    /// <summary>
    /// 读者自身的操作人标识，用于放行 <see cref="OperationVisibility.Actor"/> 层的记录。
    /// </summary>
    /// <remarks>
    /// 为 <see langword="null"/> 时 <c>Actor</c> 层一律不可见——匿名或机器主体没有"本人"可言，
    /// 放行会让自助改密这类只关乎本人的记录漏给别人。
    /// </remarks>
    public string? ActorId { get; }

    /// <summary>
    /// 读者自身所属的租户（宿主主体为 <see langword="null"/>），与 <see cref="ActorId"/> 一起认定"本人"。
    /// </summary>
    /// <remarks>
    /// 主体标识只在签发它的那一层内唯一：宿主主体进入租户操作时记录留在租户层，操作人所属租户却是宿主，
    /// 只比标识的话，租户里恰好同标识的主体会把它当成自己的记录。
    /// </remarks>
    public Guid? ActorTenantId { get; }

    /// <summary>宿主视角：所有层级都可见。</summary>
    public static OperationRecordVisibilityScope Host { get; } = new(true, true, null, null);

    /// <summary>
    /// 不加可见性限制，供不代表某个读者的内部任务使用（如归档、导出到运维系统）。
    /// </summary>
    /// <remarks>面向用户的查询应使用 <see cref="Host"/> 或 <see cref="ForTenantReader"/>。</remarks>
    public static OperationRecordVisibilityScope Unrestricted { get; } = new(false, true, null, null);

    /// <summary>
    /// 租户视角：看不到 <c>Host</c> 层；<c>Actor</c> 层仅当记录的操作人是本人（标识与所属租户都相同）。
    /// </summary>
    /// <param name="actorId">读者自身的操作人标识；未知时传 <see langword="null"/>。</param>
    /// <param name="actorTenantId">读者自身所属的租户；宿主主体传 <see langword="null"/>。</param>
    public static OperationRecordVisibilityScope ForTenantReader(string? actorId, Guid? actorTenantId)
        => new(true, false, string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim(), actorTenantId);
}
