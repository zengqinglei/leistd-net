namespace Leistd.Disposables;

/// <summary>
/// 在释放时执行一次指定操作。
/// </summary>
/// <remarks>
/// 可用于释放时恢复父上下文。重复调用 <c>Dispose()</c> 不会再次执行动作。
/// </remarks>
/// <param name="action">释放时执行的动作</param>
public sealed class DisposeAction(Action action) : IDisposable
{
    private Action? _action = action ?? throw new ArgumentNullException(nameof(action));

    /// <inheritdoc />
    public void Dispose()
    {
        // Interlocked 取出并置空：并发或重复释放都只有一方拿到非 null
        var pending = Interlocked.Exchange(ref _action, null);
        pending?.Invoke();
    }
}
