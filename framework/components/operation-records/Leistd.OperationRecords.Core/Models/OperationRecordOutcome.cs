namespace Leistd.OperationRecords.Models;

/// <summary>
/// 一次操作的结果。
/// </summary>
/// <remarks>
/// 刻意只有两档。"部分成功""重试中"这类中间态属于业务流程状态，该由业务自己的实体表达；
/// 塞进审计表会让"这次操作到底成没成"失去唯一答案，而那正是这张表存在的理由。
/// </remarks>
public enum OperationRecordOutcome
{
    /// <summary>操作完成。</summary>
    Succeeded,

    /// <summary>操作被拒绝或失败：权限不足（授权阶段）、业务规则拒绝，或执行中抛错。</summary>
    Failed
}
