using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 租户落值拦截器：新增的 <see cref="IMultiTenant"/> 实体在保存时自动填充当前租户 Id
/// </summary>
/// <remarks>
/// <para>仅处理 <c>Added</c> 且 <c>TenantId</c> 仍为 <c>null</c> 的实体——
/// 聚合显式赋过值的不覆盖；宿主上下文（无租户）保持 <c>null</c> 即宿主数据。</para>
/// <para>宿主需将本拦截器经 <c>DbContextOptionsBuilder.AddInterceptors</c> 显式挂载（与审计拦截器同型）。</para>
/// </remarks>
public class MultiTenantSaveChangesInterceptor(ICurrentTenant currentTenant) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampTenantId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampTenantId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampTenantId(DbContext? context)
    {
        if (context is null || currentTenant.Id is not { } tenantId)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added &&
                entry.Entity is IMultiTenant { TenantId: null })
            {
                entry.Property(nameof(IMultiTenant.TenantId)).CurrentValue = tenantId;
            }
        }
    }
}
