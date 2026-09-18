using Leistd.OperationRecords.Abstractions;

namespace CompanyName.ProjectName.Infrastructure.OperationRecords;

/// <summary>
/// 操作记录的归档形态：列与 <c>OperationRecord</c> 逐一对齐，外加一列归档时刻。
/// </summary>
/// <remarks>
/// <para><b>刻意不实现 <c>IMultiTenant</c>。</b>实现它会让这张表被套上租户全局过滤器，
/// 而归档作业跑在<b>无租户上下文</b>里——宿主视角下该过滤器只放行宿主自己的行，
/// 于是「读归档」会静默只看到宿主那部分。<c>TenantId</c> 因此存成普通列。
/// <b>将来若为归档表开放查询接口，必须在那一层自己按租户过滤</b>，
/// 这里没有过滤器替它兜底。</para>
/// <para>列定义与 <c>OperationRecord</c> 必须保持一致：搬运是逐字段复制，
/// 少一列就是<b>静默丢数据</b>——搬完照样报成功，缺的那列到查归档时才会发现。</para>
/// </remarks>
public class OperationRecordArchive
{
    /// <summary>主键：沿用原记录的 Id，便于按同一标识回溯。</summary>
    public Guid Id { get; set; }

    /// <summary>原记录所属租户；<b>不是</b>多租户过滤维度，见类型说明。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>业务动作码。</summary>
    public string Action { get; set; } = default!;

    /// <summary>操作目标标识；无目标时为 <c>-</c>。</summary>
    public string TargetId { get; set; } = default!;

    /// <summary>授权依据。</summary>
    public string AuthorizationBasis { get; set; } = default!;

    /// <summary>操作结果。</summary>
    public OperationRecordOutcome Outcome { get; set; }

    /// <summary>原记录的发生时间（UTC）。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>搬入归档表的时刻（UTC）。</summary>
    /// <remarks>与 <see cref="CreationTime"/> 分开存：前者是事情何时发生，后者是何时被搬走。</remarks>
    public DateTime ArchivedTime { get; set; }

    /// <summary>操作人标识。</summary>
    public string? ActorId { get; set; }

    /// <summary>操作人显示名快照。</summary>
    public string? ActorName { get; set; }

    /// <summary>模拟登录时的真实操作人标识。</summary>
    public string? ImpersonatorId { get; set; }

    /// <summary>模拟登录时真实操作人的显示名快照。</summary>
    public string? ImpersonatorName { get; set; }

    /// <summary>链路标识。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>操作目标的人类可读名快照。</summary>
    public string? TargetName { get; set; }

    /// <summary>这条记录原本能被谁看见。</summary>
    public OperationVisibility Visibility { get; set; }

    /// <summary>失败原因的稳定错误码。</summary>
    public string? FailureCode { get; set; }

    /// <summary>失败原因的本地化占位参数（JSON）。</summary>
    public string? FailureData { get; set; }

    /// <summary>面向排查的技术说明。</summary>
    public string? FailureDetail { get; set; }
}
