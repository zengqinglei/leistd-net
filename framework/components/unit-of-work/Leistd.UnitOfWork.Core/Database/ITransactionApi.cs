namespace Leistd.UnitOfWork.Database;

/// <summary>
/// 表示由工作单元提交并释放的事务资源。
/// </summary>
public interface ITransactionApi : IDisposable
{
    /// <summary>
    /// 提交事务；该边界不接受调用方取消。
    /// </summary>
    /// <remarks>
    /// <c>CompleteAsync</c> 在调用本方法之前完成最后一次取消检查；本方法开始后不响应调用方取消。
    /// 需要限制提交耗时用命令超时（工作单元的 <c>Timeout</c> 选项），它不替代取消边界。
    /// 两阶段边界的完整取舍见 unit-of-work 组件文档。
    /// </remarks>
    Task CommitAsync();
}
