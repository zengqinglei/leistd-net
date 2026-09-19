namespace Leistd.OperationRecords.Abstractions;

/// <summary>
/// 一次操作的严重度，用于把"值得盯着的事"从流水里拣出来。
/// </summary>
/// <remarks>
/// 它<b>不表达结果好坏</b>——那是 <see cref="OperationRecordOutcome"/> 的事。
/// 严重度描述的是"这个动作本身有多要紧"：授予权限无论成败都值得看一眼，
/// 而改个显示名即使失败也不必惊动任何人。
/// </remarks>
public enum OperationSeverity
{
    /// <summary>常规变更。</summary>
    Info,

    /// <summary>值得留意，但不必告警。</summary>
    Notice,

    /// <summary>
    /// 改变了"谁能做什么"或"谁是谁"，需要能直接接告警。
    /// </summary>
    /// <remarks>
    /// 权限授予变更、角色替换、关闭 MFA、开始模拟登录都属于这一档。
    /// 判据是：这个动作发生后，某个主体的能力边界或身份发生了变化。
    /// </remarks>
    Critical
}

/// <summary>
/// 一条操作记录能被谁看见。
/// </summary>
/// <remarks>
/// <para><b>可见性是审计表的安全边界，不是展示偏好。</b>多租户下这张表由租户管理员直接阅读，
/// 层级判错就是越权——而且只在真的建了租户之后才暴露。</para>
/// <para>字段级也受它约束：技术异常原文与链路标识只对宿主开放，它们可能带表名、
/// 内部地址与主机名，属于宿主的基础设施形态。</para>
/// </remarks>
public enum OperationVisibility
{
    /// <summary>该租户的管理员与宿主都能看见。</summary>
    Tenant,

    /// <summary>仅宿主可见。</summary>
    Host,

    /// <summary>
    /// 仅操作人本人可见，宿主与租户管理员另按上面两档判定。
    /// </summary>
    /// <remarks>自助改密、绑定 MFA 这类"只关乎本人"的动作用它。</remarks>
    Actor
}
