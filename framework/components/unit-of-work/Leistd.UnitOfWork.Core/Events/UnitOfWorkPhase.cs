namespace Leistd.UnitOfWork.Events;

/// <summary>
/// 指定工作单元事件的执行阶段。
/// </summary>
/// <remarks>
/// <para>只有两个阶段，且都在 <c>CompleteAsync()</c> 内被 <c>await</c>：处理器可以安全地解析
/// 作用域依赖，异常也不会消失在后台任务里。</para>
/// 不提供回滚后或释放后阶段；失败补偿使用 Outbox 或显式 <c>try/catch</c>完成。
/// </remarks>
public enum UnitOfWorkPhase
{
    /// <summary>
    /// 提交前（SaveChanges 之后、Commit 之前）。
    /// </summary>
    /// <remarks>
    /// <b>事务型</b>工作单元下，此阶段的处理器抛出异常会导致事务回滚。
    /// <b>非事务型</b>（<c>IsTransactional = false</c>）没有开启事务，之前执行的每次 SaveChanges
    /// 都已各自落库，处理器抛出只会让 <c>CompleteAsync</c> 失败，<b>不撤回已落库的写入</b>。
    /// </remarks>
    BeforeCommit,

    /// <summary>
    /// 提交后（Commit 成功之后）——默认阶段。
    /// </summary>
    /// <remarks>
    /// 事务已经提交，此阶段的处理器<b>无法</b>再回滚它；异常会原样上抛给
    /// <c>CompleteAsync()</c> 的调用方，但数据已经落库。因此这里只适合"失败了可以重试或可以丢"的
    /// 副作用（发通知、失效缓存）；必须与事务同生共死的外部动作应走 Outbox。
    /// </remarks>
    AfterCommit
}
