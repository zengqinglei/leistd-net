namespace Leistd.OperationRecords.Recording;

/// <summary>
/// 记录当前作用域内哪些动作与目标已经留过失败记录，供端点兜底据此跳过重复补记。
/// </summary>
/// <remarks>
/// 端点兜底据此跳过重复补记，读取方只有它。<see cref="IOperationRecorder.RecordFailedAsync"/>
/// 自身不判重，直接调用两次仍会写两条。使用取舍见组件文档。
/// <para>
/// 判据是动作码加目标：同一动作对不同目标的失败是不同的事实，都要留。兜底推不出目标时
/// （端点没声明目标路由键，或某一段缺失）传 null，此时退回只按动作码判——它无从分辨是哪一个目标，
/// 宁可少补一条，也不要在应用服务已经记过时再写一条重复的。
/// </para>
/// <para>
/// 因此端点注解的目标必须与应用服务记录的目标逐字一致，这本来就是按目标检索能查全的前提。
/// </para>
/// <para>
/// 按作用域注册：登记方与读取方必须在同一个 DI 作用域，从子作用域解析会拿到另一份实例。
/// 应用服务按构造注入拿记录器即可（解析自请求作用域）。
/// </para>
/// <para>
/// 一个作用域视为一条逻辑执行流，内部集合不是线程安全的；同一作用域内并发留痕要调用方自己串行化。
/// </para>
/// <para>
/// 自定义 <see cref="IOperationRecorder"/> 实现要在成功写出失败记录之后调用
/// <see cref="MarkRecorded"/>，兜底才认得出已经记过；写库失败时不要登记，让兜底补记。
/// </para>
/// </remarks>
public sealed class RecordedFailureTracker
{
    private readonly Dictionary<string, HashSet<string>> _targetsByAction = new(StringComparer.Ordinal);

    /// <summary>
    /// 登记一条已经成功写出的失败记录。
    /// </summary>
    /// <param name="action">已留痕的动作码。</param>
    /// <param name="targetId">这条记录的目标标识。</param>
    public void MarkRecorded(string action, string targetId)
    {
        if (!_targetsByAction.TryGetValue(action, out var targets))
        {
            targets = new HashSet<string>(StringComparer.Ordinal);
            _targetsByAction[action] = targets;
        }

        targets.Add(targetId);
    }

    /// <summary>
    /// 判断本作用域内该动作对该目标是否已经留过失败记录。
    /// </summary>
    /// <param name="action">要判断的动作码。</param>
    /// <param name="targetId">
    /// 要判断的目标标识；传 <c>null</c> 表示调用方推不出目标，此时该动作留过任何一条记录都算已记。
    /// </param>
    /// <returns>已经留过失败记录时返回 true。</returns>
    public bool AlreadyRecorded(string action, string? targetId)
        => _targetsByAction.TryGetValue(action, out var targets)
            && (targetId is null || targets.Contains(targetId));
}
