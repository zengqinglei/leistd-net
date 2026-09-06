using Leistd.Disposables;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Services;

/// <summary>
/// 通过 <see cref="ICurrentTenantAccessor"/> 提供当前租户上下文。
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
            accessor.Current = parent;
        });
    }
}
