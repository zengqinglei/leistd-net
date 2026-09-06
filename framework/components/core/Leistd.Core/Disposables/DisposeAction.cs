namespace Leistd.Disposables;

/// <summary>
/// 在释放时执行一次指定操作。
/// </summary>
/// <remarks>
/// 典型用法是各种 <c>Change()</c> 的返回值（当前主体、当前租户、数据过滤器开关）：
/// 调用方 <c>using</c> 住返回值，作用域退出即还原父上下文。
/// <b>幂等</b>：动作只执行一次，重复 <c>Dispose()</c> 无副作用——还原动作往往是弹栈或写回父值，
/// 执行两次会把外层作用域的状态也一并还原掉。
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
