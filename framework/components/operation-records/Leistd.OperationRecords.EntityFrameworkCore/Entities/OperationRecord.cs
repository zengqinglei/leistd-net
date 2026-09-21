using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;

namespace Leistd.OperationRecords.EntityFrameworkCore.Entities;

/// <summary>
/// 操作记录的持久化形态。
/// </summary>
/// <remarks>
/// <para><b>刻意不实现 <c>ICreationAuditedObject</c>。</b>那套审计属性由宿主挂的
/// <c>AuditSaveChangesInterceptor</c> 填充：<c>CreatorId</c> 取当前用户标识，而本表要的是
/// <see cref="ActorName"/> 这种<b>快照</b>——用户改名或销号之后仍要留住当时的名字。
/// 两套并存就有两个"什么人"的事实源，且其中一个会随宿主有没有挂拦截器而时有时无。</para>
/// <para>写入后不再修改：没有修改与删除审计列，也没有对应的存储方法。</para>
/// </remarks>
public class OperationRecord : IMultiTenant
{
    /// <summary>主键（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>操作发生时的租户上下文；<see langword="null"/> 表示宿主。</summary>
    public Guid? ActorTenantId { get; set; }

    /// <summary>业务动作码。</summary>
    public string Action { get; set; } = default!;

    /// <summary>操作目标标识；无目标时为 <c>-</c>。</summary>
    public string TargetId { get; set; } = default!;

    /// <summary>授权依据：权限名，或非权限体系的标记。</summary>
    public string AuthorizationBasis { get; set; } = default!;

    /// <summary>操作结果。</summary>
    public OperationRecordOutcome Outcome { get; set; }

    /// <summary>发生时间（UTC）。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>操作人标识；机器主体为 <see langword="null"/>。</summary>
    public string? ActorId { get; set; }

    /// <summary>操作人显示名的快照。</summary>
    public string? ActorName { get; set; }

    /// <summary>模拟登录时的真实操作人标识。</summary>
    public string? ImpersonatorId { get; set; }

    /// <summary>模拟登录时真实操作人的显示名快照。</summary>
    public string? ImpersonatorName { get; set; }

    /// <summary>链路标识，用于回查当次请求的完整日志。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>操作目标的人类可读名快照；取不到时为 <see langword="null"/>。</summary>
    public string? TargetName { get; set; }

    /// <summary>这条记录能被谁看见；写入时由动作定义盖章，查询按它过滤。</summary>
    public OperationVisibility Visibility { get; set; } = OperationVisibility.Host;

    /// <summary>失败原因的稳定错误码，兼本地化资源键。</summary>
    public string? FailureCode { get; set; }

    /// <summary>失败原因的本地化占位参数（JSON）。</summary>
    public string? FailureData { get; set; }

    /// <summary>面向排查的技术说明，仅宿主可见。</summary>
    public string? FailureDetail { get; set; }

    /// <summary>由传输形态创建实体。</summary>
    /// <param name="info">记录内容。</param>
    public static OperationRecord FromInfo(OperationRecordInfo info) => new()
    {
        Id = info.Id,
        TenantId = info.TenantId,
        ActorTenantId = info.ActorTenantId,
        Action = Truncate(info.Action, OperationRecordInfo.MaxActionLength)!,
        TargetId = Truncate(info.TargetId, OperationRecordInfo.MaxTargetIdLength)!,
        AuthorizationBasis = Truncate(info.AuthorizationBasis, OperationRecordInfo.MaxAuthorizationBasisLength)!,
        Outcome = info.Outcome,
        CreationTime = info.CreationTime,
        ActorId = Truncate(info.ActorId, OperationRecordInfo.MaxActorIdLength),
        ActorName = Truncate(info.ActorName, OperationRecordInfo.MaxActorNameLength),
        ImpersonatorId = Truncate(info.ImpersonatorId, OperationRecordInfo.MaxActorIdLength),
        ImpersonatorName = Truncate(info.ImpersonatorName, OperationRecordInfo.MaxActorNameLength),
        CorrelationId = Truncate(info.CorrelationId, OperationRecordInfo.MaxCorrelationIdLength),
        TargetName = Truncate(info.TargetName, OperationRecordInfo.MaxTargetNameLength),
        Visibility = info.Visibility,
        FailureCode = Truncate(info.FailureCode, OperationRecordInfo.MaxFailureCodeLength),
        // FailureData 不截断：它是 JSON，截断会得到一个无法解析的串——
        // 那比不存更糟，因为读取方拿到的是"看起来有值但解析必然失败"的数据。
        FailureData = info.FailureData,
        FailureDetail = Truncate(info.FailureDetail, OperationRecordInfo.MaxFailureDetailLength)
    };

    /// <summary>转换为传输形态。</summary>
    public OperationRecordInfo ToInfo() => new()
    {
        Id = Id,
        TenantId = TenantId,
        ActorTenantId = ActorTenantId,
        Action = Action,
        TargetId = TargetId,
        AuthorizationBasis = AuthorizationBasis,
        Outcome = Outcome,
        CreationTime = CreationTime,
        ActorId = ActorId,
        ActorName = ActorName,
        ImpersonatorId = ImpersonatorId,
        ImpersonatorName = ImpersonatorName,
        CorrelationId = CorrelationId,
        TargetName = TargetName,
        Visibility = Visibility,
        FailureCode = FailureCode,
        FailureData = FailureData,
        FailureDetail = FailureDetail
    };

    /// <summary>操作人标识列长度上限：容得下 GUID 字符串与机器主体的 client_id。</summary>
    /// <remarks>
    /// <b>指向 <see cref="OperationRecordInfo.MaxActorIdLength"/>，不再独立取值。</b>
    /// 此前两处各写一份 128，而用法已经分叉：<see cref="FromInfo"/> 截断按 Core 那份、
    /// EF 列长度配置按这一份。值相同时相安无事，改任一处就会让截断长度与列长度静默错开——
    /// 超出的部分要么被数据库拒绝、要么被二次截断。长度是同一个事实，只能有一个源。
    /// </remarks>
    public const int MaxActorIdLength = OperationRecordInfo.MaxActorIdLength;

    // 就地截断而不是抛异常：审计写入不能因为一个字段超长，把一次已经成功的业务操作变成 500。
    // Recorder 在写入前已对超长值记 Warning，排查时按那条日志走。
    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
