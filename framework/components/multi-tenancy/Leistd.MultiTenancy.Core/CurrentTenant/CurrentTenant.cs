namespace Leistd.MultiTenancy;

/// <summary>
/// <see cref="ICurrentTenant"/> 默认实现，读写全部委托给 <see cref="ICurrentTenantAccessor"/>
/// </summary>
public class CurrentTenant(ICurrentTenantAccessor accessor) : ICurrentTenant
{
    /// <inheritdoc />
    public bool IsAvailable => Id.HasValue;

    /// <inheritdoc />
    public Guid? Id => accessor.Current?.TenantId;

    /// <inheritdoc />
    public string? Name => accessor.Current?.Name;

    /// <inheritdoc />
    public IDisposable Change(Guid? id, string? name = null)
    {
        var parent = accessor.Current;
        accessor.Current = new BasicTenantInfo(id, name);

        return new DisposeAction(() =>
        {
            accessor.Current = parent; // 自动恢复父上下文
        });
    }
}

/// <summary>
/// Dispose 动作包装器
/// </summary>
/// <param name="action">要执行的动作</param>
file sealed class DisposeAction(Action action) : IDisposable
{
    private Action? _action = action ?? throw new ArgumentNullException(nameof(action));

    public void Dispose()
    {
        var action = Interlocked.Exchange(ref _action, null);
        action?.Invoke();
    }
}
