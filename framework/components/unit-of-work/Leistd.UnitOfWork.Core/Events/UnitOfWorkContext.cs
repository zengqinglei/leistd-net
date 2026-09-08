namespace Leistd.UnitOfWork.Events;

/// <summary>
/// 保存当前异步流的工作单元事件阶段。
/// </summary>
public static class UnitOfWorkContext
{
    private static readonly AsyncLocal<UnitOfWorkPhase?> _currentPhase = new();

    /// <summary>
    /// 当前工作单元阶段（用于事件处理器过滤）
    /// </summary>
    public static UnitOfWorkPhase? CurrentPhase => _currentPhase.Value;

    // 进入一个阶段，返回时还原进入前的值——阶段是可嵌套作用域，不是"进入即置位、退出即清空"。
    //
    // 嵌套是真实形态：BeforeCommit 处理器里可以开一个 requiresNew 的独立工作单元，
    // 它自己也会走一遍阶段。退出时清空会让外层剩余处理器读到 null，
    // 于是 AfterCommit 处理器提前执行、BeforeCommit 处理器被跳过。
    internal static IDisposable EnterPhase(UnitOfWorkPhase phase)
    {
        var previous = _currentPhase.Value;
        _currentPhase.Value = phase;
        return new PhaseScope(previous);
    }

    private sealed class PhaseScope(UnitOfWorkPhase? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _currentPhase.Value = previous;
            _disposed = true;
        }
    }
}
