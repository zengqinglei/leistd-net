using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Stores;

/// <summary>
/// 直接读控制库列出租户的连接登记。
/// </summary>
/// <remarks>
/// 从未删除租户的入口起查：控制面上下文没有软删除过滤器兜底，少这道 join 就会把已删租户的登记也列出来。
/// 只投影名字与版本，密文列根本不进 SELECT。
/// </remarks>
/// <typeparam name="TDbContext">映射了租户注册表的控制库上下文。</typeparam>
/// <param name="dbContextProvider">控制库上下文。</param>
public class EfCoreTenantConnectionDirectory<TDbContext>(IDbContextProvider<TDbContext> dbContextProvider)
    : ITenantConnectionDirectory
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantConnectionEntry>?> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        // 租户不存在与"存在但没登记"必须分开：前者让调用方回 404，后者是合法的"不分库"
        if (!await dbContext.UndeletedTenants().AsNoTracking().AnyAsync(x => x.Id == tenantId, cancellationToken))
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
