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
    // 独立嵌套工作单元退出时恢复外层阶段，不能直接清空。
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
