namespace Leistd.OperationRecords.Models;

/// <summary>
/// 操作的目标：<b>对谁做的</b>。标识与名字快照捆绑传递。
/// </summary>
/// <remarks>
/// <para><b>为什么是一个类型而不是两个参数。</b>记录方法原本已有三个连续的字符串参数
/// （动作码、目标标识、授权依据），再加一个目标名就是四个——相邻两个互换不会报错、
/// 不会有任何症状，只会让审计表里的目标名列悄悄存进权限名。把标识与名字收进一个类型，
/// 既让它们不可能一个传了一个忘，也让它与授权依据在类型上不可互换。</para>
/// <para><b>名字是快照，不是外键。</b>理由与 <see cref="OperationRecordInfo.ActorName"/> 逐字相同：
/// 改名或销号之后靠标识反查，得到的要么是新名字、要么什么都没有，
/// 而审计要回答的是"当时是什么"。主流同型：Django <c>LogEntry</c> 同时存
/// <c>object_id</c> 与 <c>object_repr</c>；Jira changelog 存 <c>from</c>/<c>fromString</c>。</para>
/// </remarks>
public readonly record struct OperationTarget
{
    /// <summary>没有目标时的占位标识。</summary>
    /// <remarks>
    /// 与"授权依据缺失"的占位<b>不是同一个概念</b>，即便字面值相同：
    /// 一个回答"对谁做的"，一个回答"凭什么"。各自独立，改其中一个不该动到另一个。
    /// </remarks>
    public const string NoTargetId = "-";

    private OperationTarget(string id, string? name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>目标标识；没有目标时为 <see cref="NoTargetId"/>。</summary>
    public string Id { get; }

    /// <summary>
    /// 目标的人类可读名快照；取不到时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// <b>授权阶段的拒绝拿不到名字，这是正确的，不是将就。</b>那条路径上调用方正因为
    /// 无权访问该目标而被拒；框架若为了凑一句好看的话去查名字回填，
    /// 等于把调用方无权查看的名字写进了他能读到的记录里。
    /// </remarks>
    public string? Name { get; }

    /// <summary>没有目标的操作（如创建类动作在授权阶段被拒时）。</summary>
    public static OperationTarget None { get; } = new(NoTargetId, null);

    /// <summary>由标识与可选的名字快照构造。</summary>
    /// <param name="id">目标标识；为空白时退化为 <see cref="None"/>。</param>
    /// <param name="name">名字快照。取值规则由业务定，但同类记录必须一致——
    /// 一半存登录名一半存显示名，会让同一张表里的同类行长得不一样。</param>
    public static OperationTarget For(string? id, string? name = null)
        => string.IsNullOrWhiteSpace(id) ? None : new(id.Trim(), Normalize(name));

    /// <summary>由 <see cref="Guid"/> 标识与可选的名字快照构造。</summary>
    public static OperationTarget For(Guid id, string? name = null)
        => new(id.ToString(), Normalize(name));

    // 空白名字一律收敛成 null：落库一个空串之后，"这个目标没有名字"
    // 与"调用方没传名字"就再也分不开了。
    private static string? Normalize(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}
