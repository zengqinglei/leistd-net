namespace Leistd.OperationRecords.Abstractions;

/// <summary>
/// 一条操作记录：<b>什么人、在什么时间、做了什么、结果如何</b>。
/// </summary>
/// <remarks>
/// <para>字段刻意只有这些。每一列都要能回答上面四问之一（或"凭什么"这第五问），
/// 回答不了的一律不进——审计表一旦开始容纳"顺手也记一下"的东西，就会长成第二份业务日志，
/// 既查不快也没人信。</para>
/// <para>请求维度的信息（IP、UA、URL）属于请求日志，变更明细属于实体变更追踪，
/// 两者都不在这里：<see cref="CorrelationId"/> 已经把它们挂上了同一条链路，按它回查即可。</para>
/// </remarks>
public sealed class OperationRecordInfo
{
    /// <summary>动作码长度上限。</summary>
    public const int MaxActionLength = 120;

    /// <summary>目标标识长度上限。</summary>
    public const int MaxTargetIdLength = 160;

    /// <summary>授权依据长度上限。</summary>
    public const int MaxAuthorizationBasisLength = 160;

    /// <summary>操作人名长度上限。</summary>
    public const int MaxActorNameLength = 160;

    /// <summary>操作人标识长度上限：容得下 GUID 字符串与机器主体的 client_id。</summary>
    public const int MaxActorIdLength = 128;

    /// <summary>链路标识长度上限。</summary>
    public const int MaxCorrelationIdLength = 64;

    /// <summary>目标名快照长度上限：与操作人名同量级，两者都是显示名。</summary>
    public const int MaxTargetNameLength = 160;

    /// <summary>失败错误码长度上限：与授权依据同量级，两者都是 <c>Xxx:Yyy</c> 形态的码。</summary>
    public const int MaxFailureCodeLength = 160;

    /// <summary>
    /// 失败技术说明长度上限。
    /// </summary>
    /// <remarks>
    /// 容得下一句完整描述，但必须有界：技术说明来自调用方手写，无界列会让一次
    /// 误传堆栈把审计表撑爆，而这张表是写多读少、要长期留存的。
    /// </remarks>
    public const int MaxFailureDetailLength = 1024;

    /// <summary>记录标识（有序 Guid v7，本身即按写入时间单调）。</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>记录所在的层：<see langword="null"/> 表示宿主层，否则是该租户的层。</summary>
    /// <remarks>
    /// 通常就是操作发生时的租户上下文；唯一的例外是 <see cref="OperationVisibility.Host"/> 的失败记录——
    /// 它们一律写进宿主层（此值为 <see langword="null"/>），来源租户由 <see cref="ActorTenantId"/> 保留。
    /// 否则一条"租户用户试图调用宿主接口被拒"的记录会留在租户层，租户读者按可见性看不到它，
    /// 宿主又按租户维度查不到它，谁都看不见——而那正是宿主最该看到的安全事件。
    /// </remarks>
    public Guid? TenantId { get; init; }

    /// <summary>操作发生时的租户上下文；<see langword="null"/> 表示宿主。</summary>
    /// <remarks>
    /// 回答"什么人"的另一半：<see cref="ActorId"/> 只在它所属的租户里有意义。
    /// 与 <see cref="TenantId"/> 不同的行，就是从租户上下文写进宿主层的那些记录。
    /// </remarks>
    public Guid? ActorTenantId { get; init; }

    /// <summary>做了什么：业务动作码，如 <c>identity.user.created</c>。</summary>
    /// <remarks>
    /// 由业务定义并保持稳定——界面按它本地化。<b>不要用 HTTP 方法与路由代替</b>：
    /// 路由会改，而"发生过什么"不该随之改写；同一个路由也可能承载多个业务含义。
    /// </remarks>
    public required string Action { get; init; }

    /// <summary>对谁做的：目标标识；没有目标时为 <c>-</c>。</summary>
    public required string TargetId { get; init; }

    /// <summary>对谁做的：目标的人类可读名<b>快照</b>；取不到时为 <see langword="null"/>。</summary>
    /// <remarks>
    /// <para>存快照的理由与 <see cref="ActorName"/> 逐字相同：改名或销号之后靠
    /// <see cref="TargetId"/> 反查，得到的要么是新名字、要么什么都没有，
    /// 而审计要回答的是"当时是什么"。</para>
    /// <para><b>授权阶段的拒绝没有名字，这是正确的。</b>那条路径上调用方正因无权访问该目标
    /// 而被拒，框架若去查名字回填，等于把他无权查看的名字写进了他能读到的记录里。</para>
    /// </remarks>
    public string? TargetName { get; init; }

    /// <summary>凭什么：授权依据，由业务定义。</summary>
    /// <remarks>
    /// 通常是权限名。不由权限把守的操作（用户改自己的密码、机器主体凭 scope 调用）
    /// 传业务自己的标记——框架只存不读，因此不替业务枚举有哪几种。
    /// </remarks>
    public required string AuthorizationBasis { get; init; }

    /// <summary>结果如何。</summary>
    public required OperationRecordOutcome Outcome { get; init; }

    /// <summary>
    /// 这条记录能被谁看见，写入时由动作定义盖章。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要把可见性反规范化到记录上。</b>可见性本身定义在动作上
    /// （<see cref="IOperationActionDefinition.Visibility"/>），但查询必须能在数据库里按它过滤——
    /// 查出来再内存过滤会让总数与当页双双算错，分页直接失效。</para>
    /// <para><b>必填、没有默认值。</b>可见性是安全边界，任何默认值都是替写入方做的决定：
    /// 默认给租户看是泄露，默认只给宿主看又会让租户层的记录谁都看不见。</para>
    /// </remarks>
    public required OperationVisibility Visibility { get; init; }

    /// <summary>为什么没成：失败原因的稳定错误码，兼本地化资源键。</summary>
    /// <remarks>
    /// 存码而不是存渲染好的句子——写入时是哪国语言，此后所有读者看到的就是哪国语言，
    /// 改不回来。展示期按读者当前语言渲染，与 <see cref="Action"/> 是同一套路子。
    /// </remarks>
    public string? FailureCode { get; init; }

    /// <summary>失败原因的本地化占位参数，序列化为 JSON 对象。</summary>
    /// <remarks>只服务于失败原因的文案模板；动作句子的占位符不因此扩张。</remarks>
    public string? FailureData { get; init; }

    /// <summary>面向排查的技术说明，不本地化。</summary>
    /// <remarks>
    /// <b>仅宿主可见</b>：可能带表名、内部地址与主机名，属于宿主的基础设施形态。
    /// 写入方必须是显式传入的可公开说明，不是原始异常文本——见 <see cref="OperationFailure"/>。
    /// </remarks>
    public string? FailureDetail { get; init; }

    /// <summary>什么时间（UTC）。</summary>
    public DateTime CreationTime { get; init; }

    /// <summary>什么人：操作人标识，取主体声明的原始值；匿名请求为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 不要求是 GUID：机器主体（<c>client:&lt;client_id&gt;</c>）与后台作业主体的 <c>sub</c> 原样记下，
    /// 否则这两类操作会全部记成无主的。
    /// </remarks>
    public string? ActorId { get; init; }

    /// <summary>什么人：操作人显示名的<b>快照</b>。</summary>
    /// <remarks>
    /// 存快照而不是每次联表取现名：用户改名或销号之后，靠 <see cref="ActorId"/> 反查到的
    /// 要么是新名字、要么什么都没有，而审计要回答的是"当时是谁"。
    /// </remarks>
    public string? ActorName { get; init; }

    /// <summary>模拟登录时的<b>真实</b>操作人标识；非模拟场景为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 有值即表示这次操作是宿主管理员以租户身份做的：<see cref="ActorId"/> 是被模拟的租户用户，
    /// 而真正按下按钮的是这里的人。两个都留，事后追责才指得到正确的人。
    /// </remarks>
    public string? ImpersonatorId { get; init; }

    /// <summary>模拟登录时真实操作人的显示名快照。</summary>
    public string? ImpersonatorName { get; init; }

    /// <summary>链路标识，与请求日志、响应头同源，用于从记录反查当次请求的全部细节。</summary>
    public string? CorrelationId { get; init; }
}
