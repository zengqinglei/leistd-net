using Leistd.UnitOfWork.EfCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 基于 EF Core 的租户存储，每次查询都读数据库
/// </summary>
/// <remarks>
/// <para><b>刻意不缓存。</b>本存储的返回值里带 <see cref="TenantConfiguration.IsActive"/>，
/// 中间件据此放行或 403——它是**访问控制状态**，不是普通读模型。而 cache-aside 的失效是
/// 尽力而为的：缓存不可用时失效会失败，于是"已停用/已删除"的租户仍被陈旧条目放行，
/// 且删除之后连重试失效都做不到（租户已软删，管理器按 Id 找不到它）。缩短 TTL 只能缩窄
/// 这个窗口，关不掉它。直接读库让停用与删除**由构造保证立即生效**。</para>
/// <para>成本可忽略：租户解析每请求一次主键/唯一索引查找，落在数据库缓冲池里；
/// 而且用的是当前请求已有的 <typeparamref name="TDbContext"/>，不新开连接。
/// 同一个仓库里用户判活（<c>ActiveUserRequirement</c>）本来就是每请求读库，
/// 租户表比用户表更小更热，没有理由反而给它加一层陈旧风险。</para>
/// <para>附带收益：租户解析不再依赖分布式缓存可用。此前缓存读取失败会直接抛出，
/// 意味着缓存一挂、所有带租户的请求全部 500。</para>
/// <para>确实需要为租户判活加缓存的服务（极高 RPS 且能接受撤销延迟），自行包装
/// <see cref="ITenantStore"/> 装饰器——那是一个需要显式承担陈旧风险的决定，
/// 不该由框架替所有人默认做。</para>
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
        // 显式排除软删除行：不依赖宿主 DbContext 是否配置了全局软删除过滤器
        => (await dbContextProvider.GetDbContextAsync(cancellationToken))
            .Set<TenantRecord>().AsNoTracking().Where(t => !t.IsDeleted);

    /// <summary>
    /// 持久化实体 → Core 出参。管理器与存储共用，保证两条读路径的形状一致。
    /// </summary>
    internal static TenantConfiguration ToConfiguration(TenantRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        NormalizedName = record.NormalizedName,
        DisplayName = record.DisplayName,
        IsActive = record.IsActive,
        CreationTime = record.CreationTime
    };
}
