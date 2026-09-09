using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;
using Leistd.MultiTenancy.Stores;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Stores;

/// <summary>
/// 使用 EF Core 直接查询租户配置。
/// </summary>
/// <remarks>
/// <b>刻意不缓存，每次解析都读库</b>：<see cref="TenantConfiguration.IsActive"/> 是访问控制状态，
/// 缓存不可用时已停用或已删除的租户会被陈旧条目放行。直读让停用与删除在提交那一刻对所有节点生效。
/// 成本是每请求一次索引查找，且复用当前请求已有的 <typeparamref name="TDbContext"/>。
/// 确需缓存的服务自行包装 <see cref="ITenantStore"/> 装饰器，取舍见 multi-tenancy 组件文档。
/// </remarks>
public class EfCoreTenantStore<TDbContext>(IDbContextProvider<TDbContext> dbContextProvider) : ITenantStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await (await QueryAsync(cancellationToken))
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return record is null ? null : ToConfiguration(record);
    }

    /// <inheritdoc />
    public async Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        var record = await (await QueryAsync(cancellationToken))
            .FirstOrDefaultAsync(t => t.NormalizedName == normalizedName, cancellationToken);
        return record is null ? null : ToConfiguration(record);
    }

    private async Task<IQueryable<TenantRecord>> QueryAsync(CancellationToken cancellationToken)
        => (await dbContextProvider.GetDbContextAsync(cancellationToken))
            .UndeletedTenants().AsNoTracking();

    internal static TenantConfiguration ToConfiguration(TenantRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        NormalizedName = record.NormalizedName,
        DisplayName = record.DisplayName,
        Description = record.Description,
        IsActive = record.IsActive,
        CreationTime = record.CreationTime
    };
}
