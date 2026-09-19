namespace Leistd.OperationRecords.Abstractions;

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
/// <para>默认值（<c>default</c>）表示<b>不加限制</b>：既有调用方不传它时行为与从前一致。</para>
/// </remarks>
public readonly record struct OperationRecordVisibilityScope
{
    private OperationRecordVisibilityScope(bool restricted, bool includesHostRecords, string? actorId)
    {
        IsRestricted = restricted;
        IncludesHostRecords = includesHostRecords;
        ActorId = actorId;
    }

    /// <summary>
    /// 是否施加可见性限制。<c>default</c> 时为 <see langword="false"/>，即<b>不过滤</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>字段特意取"是否受限"而不是"是否不受限"</b>：结构体的 <c>default</c> 把所有
    /// 布尔置为 <see langword="false"/>，只有这个方向才能让"什么都不传"落到"与从前一致"，
    /// 而不是落到"把所有 Host 层记录静默滤光"。反过来命名会让每个不传参数的调用方
    /// 都掉进同一个坑，且症状是"查出来是空的"这种最难联想到默认值的表现。</para>
    /// <para><b>这也意味着忘记传 <c>scope</c> 就是不过滤。</b>这个取舍是刻意的：本存储的契约是
    /// "只负责写入与查询，不做租户判定"，可见性判定属于调用方；让组件在参数缺失时
    /// 自作主张收紧，会把一个它无权做的决定藏在默认值里。真正的边界由调用方显式表达。</para>
    /// </remarks>
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

    /// <summary>宿主视角：所有层级都可见。</summary>
    public static OperationRecordVisibilityScope Host { get; } = new(true, true, null);

    /// <summary>不加限制，与不传本参数等价（即 <c>default</c>）。</summary>
    public static OperationRecordVisibilityScope Unrestricted { get; } = default;

    /// <summary>
    /// 租户视角：看不到 <c>Host</c> 层；<c>Actor</c> 层仅当记录的操作人是本人。
    /// </summary>
    /// <param name="actorId">读者自身的操作人标识；未知时传 <see langword="null"/>。</param>
    public static OperationRecordVisibilityScope ForTenantReader(string? actorId)
        => new(true, false, string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim());
}
