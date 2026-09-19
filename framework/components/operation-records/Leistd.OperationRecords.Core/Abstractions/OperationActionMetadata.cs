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

/// <summary>
/// 操作动作的类别，驱动界面的分类筛选。
/// </summary>
/// <remarks>
/// <b>刻意是字符串常量而不是枚举。</b>类别要随业务生长（下游会有"订单""结算"），
/// 枚举一旦定死，下游要么塞不进自己的类别、要么被迫改框架。
/// 这里只给框架自己会用到的几个，业务可以自定义任意值。
/// </remarks>
public static class OperationCategories
{
    /// <summary>登录、登出、改密、MFA、外部登录绑定、令牌签发。</summary>
    public const string Authentication = "authentication";

    /// <summary>用户与角色本身的增删改。</summary>
    public const string Account = "account";

    /// <summary>权限授予与角色分配——改变"谁能做什么"。</summary>
    public const string Authorization = "authorization";

    /// <summary>租户的创建、启停与连接配置。</summary>
    public const string Tenant = "tenant";

    /// <summary>设置变更。</summary>
    public const string Configuration = "configuration";

    /// <summary>业务数据的变更。</summary>
    public const string Data = "data";
}
