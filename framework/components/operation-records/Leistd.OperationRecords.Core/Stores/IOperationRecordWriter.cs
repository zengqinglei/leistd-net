using Leistd.OperationRecords.Models;

namespace Leistd.OperationRecords.Stores;

/// <summary>
/// 操作记录的写入契约：记录器经它把已定案的记录交给存储或输出端。
/// </summary>
/// <remarks>
/// <para>只负责写入，不做动作码校验与租户判定——那些在记录器里完成，到这里的记录已经冻结。
/// 一个宿主只能有一个写入实现：一半的审计写进宿主没预期的去处，比没有审计更危险。</para>
/// <para><b>没有更新与删除。</b>审计记录一旦写下就不该再改——留了入口，"清理误记录"
/// 迟早会变成"清理不想被看到的记录"，而那时这份记录已经不能作为证据了。</para>
/// </remarks>
public interface IOperationRecordWriter
{
    /// <summary>写入一条记录。</summary>
    /// <remarks>
    /// <para>结果决定与业务事务的关系，这是契约的一部分，实现必须照做：</para>
    /// <list type="bullet">
    /// <item><see cref="OperationRecordOutcome.Succeeded"/>：只在它描述的那次变更真正生效之后才可见——
    /// 有环境工作单元就跟随它，回滚或提交失败时这条记录不得出现；没有就即时生效（调用方在变更落库之后才记录）。
    /// 记录的 <see cref="OperationRecordInfo.TenantId"/> 与当前租户上下文一致。</item>
    /// <item><see cref="OperationRecordOutcome.Failed"/>：立即、独立于调用方的事务写出，不随调用方回滚——
    /// 失败记录描述的是一次没发生的变更，没有可以同生共死的对象，而业务拒绝之后几乎总是紧跟着回滚。</item>
    /// </list>
    /// <para>各实现对"生效"的持久化保证不同（数据库存储与业务同事务提交；日志输出在提交之后写出），
    /// 由实现在自己的文档里写明。</para>
    /// </remarks>
    /// <param name="record">要写入的记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default);
}
