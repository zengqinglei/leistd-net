namespace Leistd.OperationRecords.Recording;

/// <summary>
/// 记录当前作用域内哪些动作已经留过失败记录，供端点兜底据此跳过重复补记。
/// </summary>
/// <remarks>
/// 业务拒绝有两处可以留痕：应用服务在拒绝处调 <see cref="IOperationRecorder.RecordFailedAsync"/>
/// （它手里有文案参数与业务目标名），以及宿主在端点管道里兜底补记（异常冒泡之后才拿到，只有错误码与路由值）。
/// 两处都记就是一次失败两条记录。按作用域登记动作码，让兜底能认出"这个动作已经记过了"，
/// 保住先记的那条——应用服务先记、兜底后记，先到者胜正好留下信息更全的那一条。
/// <para>
/// 判据是动作码而不是"每个作用域一条"：同一次请求里出现多条不同动作的失败记录是正常的
/// （例如改口令失败之后紧跟账号被锁定），一刀切会把第二条吞掉。
/// </para>
/// <para>
/// 自定义 <see cref="IOperationRecorder"/> 实现要在成功写出失败记录之后调用
/// <see cref="MarkRecorded"/>，否则兜底认不出已经记过，同一次失败会留下两条。
/// 写库失败时不要登记：那条失败并没有留痕，应当让兜底补记。
/// </para>
/// <para>
/// 只有宿主的端点兜底会读取它；<see cref="IOperationRecorder.RecordFailedAsync"/> 自身不判重，
/// 直接调用两次仍会写两条。按作用域注册，生命周期由宿主的作用域决定。
/// </para>
/// </remarks>
public sealed class RecordedFailureTracker
{
    private readonly HashSet<string> _actions = new(StringComparer.Ordinal);

    /// <summary>
    /// 登记一条已经成功写出的失败记录。
    /// </summary>
    /// <param name="action">已留痕的动作码。</param>
    public void MarkRecorded(string action) => _actions.Add(action);

    /// <summary>
    /// 判断本作用域内该动作是否已经留过失败记录。
    /// </summary>
    /// <param name="action">要判断的动作码。</param>
    /// <returns>已经留过失败记录时返回 true。</returns>
    public bool AlreadyRecorded(string action) => _actions.Contains(action);
}
