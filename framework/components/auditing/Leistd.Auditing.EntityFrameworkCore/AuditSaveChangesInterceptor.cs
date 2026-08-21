using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Leistd.Auditing.EntityFrameworkCore;

/// <summary>
/// 修改与删除审计的填充拦截器（含软删除转换）
/// </summary>
/// <remarks>
/// <para><b>不处理新增。</b>创建审计（<c>CreationTime</c> / <c>CreatorId</c>）在实体**进入跟踪时**
/// 由 <c>BaseDbContext</c> 落定，不在这里。原因：仓储在工作单元内不立即保存，
/// 新增与保存之间可能跨越 <c>ICurrentPrincipalAccessor.Change</c> 的边界，
/// 在保存时刻取当前用户会把创建者落成外层主体；而且在保存前，实体的
/// <c>CreatorId</c> 一直是 null，保存前读它的代码（领域事件、业务校验、导出）看到的都是空。</para>
/// <para><b>修改与删除必须留在这里。</b><c>Modified</c> / <c>Deleted</c> 是状态迁移的结果，
/// 只有保存时刻才知道最终形态——跟踪事件在实体首次进入跟踪时就已触发完毕，抓不到"后来被改了"。</para>
/// <para>通过 <c>EntityEntry</c> API 直接设置属性，不使用反射，避免 EF Core 代理类兼容性问题。</para>
/// </remarks>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuditPropertySetter _auditPropertySetter;

    public AuditSaveChangesInterceptor(IAuditPropertySetter auditPropertySetter)
    {
        _auditPropertySetter = auditPropertySetter;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateAuditFields(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <summary>
    /// 根据实体状态更新修改/删除审计字段（新增不在此处理，见类型注释）
    /// </summary>
    private void UpdateAuditFields(DbContext? context)
    {
        if (context == null)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Modified:
                    // 排除仅软删除标记的情况（避免重复设置修改审计）
                    if (entry.Entity is ISoftDelete { IsDeleted: true })
                        break;
                    _auditPropertySetter.SetModificationProperties(entry);
                    break;

                case EntityState.Deleted:
                    // 处理软删除：将物理删除转换为逻辑删除
                    if (entry.Entity is ISoftDelete)
                    {
                        entry.State = EntityState.Modified;
                        _auditPropertySetter.SetDeletionProperties(entry);
                    }
                    break;
            }
        }
    }
}
