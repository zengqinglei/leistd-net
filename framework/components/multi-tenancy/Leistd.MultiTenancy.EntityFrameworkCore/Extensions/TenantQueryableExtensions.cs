using Microsoft.EntityFrameworkCore;
using Leistd.Auditing;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.Auditing.Abstractions;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Extensions;

/// <summary>
/// 提供排除已删除租户的控制面查询入口。
/// </summary>
/// <remarks>
/// 控制面上下文通常是<b>普通</b> <see cref="DbContext"/>，<b>没有全局软删除过滤器兜底</b>；
/// 软删租户的连接配置行仍在库里（级联只在硬删时触发），漏掉这道 join 就是一条越过删除边界的读路径。
/// 查这两张表<b>必须</b>从本类的入口起查。
/// </remarks>
public static class TenantQueryableExtensions
{
    /// <summary>
    /// 查询未删除的租户记录。
    /// </summary>
    public static IQueryable<TenantRecord> UndeletedTenants(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Set<TenantRecord>().Where(t => !t.IsDeleted);
    }

    /// <summary>
    /// 查询未删除租户的连接配置记录。
    /// </summary>
    /// <remarks>
    /// 用 join 而不是 <c>Where(EXISTS ...)</c>：两者等价，join 与本家族既有写法一致。
    /// 投影回连接行本身，调用方拿到的仍是 <see cref="TenantConnectionRecord"/>。
    /// </remarks>
    public static IQueryable<TenantConnectionRecord> ConnectionsOfUndeletedTenants(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Set<TenantConnectionRecord>()
            .Join(
                dbContext.UndeletedTenants(),
                connection => connection.TenantId,
                tenant => tenant.Id,
                (connection, _) => connection);
    }
}
