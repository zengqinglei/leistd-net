#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Tenants.Connections;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 直接读控制库列出租户的连接登记。
/// </summary>
/// <remarks>
/// <para>从 <c>UndeletedTenants()</c> / <c>ConnectionsOfUndeletedTenants()</c> 起查：控制面上下文是普通
/// <c>DbContext</c>，没有软删除过滤器兜底，少这道 join 就会把已删租户的登记也列出来。</para>
/// <para><b>只投影名字与版本</b>，密文列根本不进 SELECT——管理面不需要它，取出来只是多一处副本。</para>
/// </remarks>
internal sealed class TenantConnectionDirectory(IdentityControlDbContext dbContext) : ITenantConnectionDirectory
{
    public async Task<IReadOnlyList<TenantConnectionEntry>?> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // 租户不存在与"存在但没登记"必须分开：前者让调用方回 404，后者是合法的"不分库"
        var exists = await dbContext.UndeletedTenants()
            .AsNoTracking()
            .AnyAsync(x => x.Id == tenantId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .Select(x => new TenantConnectionEntry(x.Name, x.Version))
            .ToListAsync(cancellationToken);
    }
}
#endif
