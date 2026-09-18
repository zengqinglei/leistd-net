using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Stores;

/// <summary>直接读取 Identity Control DB 的租户连接存储。</summary>
/// <remarks>
/// <para>所有读都经 <see cref="TenantQueryableExtensions.ConnectionsOfUndeletedTenants"/> 或
/// <see cref="TenantQueryableExtensions.UndeletedTenants"/> 起查：已删除租户的连接行仍在库里，
/// 控制面上下文又没有软删除过滤器兜底，少这道 join 就会把路由交给一个已经被删掉的租户。</para>
/// <para>返回的连接串已用宿主的 Data Protection 密钥环解密；解密失败抛出，不回退。</para>
/// </remarks>
public class EfCoreTenantConnectionConfigurationStore<TDbContext> : ITenantConnectionConfigurationStore
    where TDbContext : DbContext
{
    private readonly IDbContextProvider<TDbContext> _dbContextProvider;
    private readonly TenantConnectionStringProtector _protector;

    /// <summary>创建存储。</summary>
    /// <param name="dbContextProvider">工作单元内的 DbContext 提供器</param>
    /// <param name="dataProtectionProvider">宿主的 Data Protection 提供器，用于解密连接串</param>
    public EfCoreTenantConnectionConfigurationStore(
        IDbContextProvider<TDbContext> dbContextProvider,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbContextProvider = dbContextProvider;
        _protector = new TenantConnectionStringProtector(dataProtectionProvider);
    }

    /// <inheritdoc />
    public async Task<TenantConnectionLookupResult?> FindAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = TenantConnectionNames.Normalize(name);
        var dbContext = await _dbContextProvider.GetDbContextAsync(cancellationToken);

        // 租户不存在与"租户存在但没登记连接"是两种结果，必须分开问：
        // 前者返回 null 让调用方失败关闭，后者要回落到服务自己的配置
        var tenantExists = await dbContext.UndeletedTenants()
            .AnyAsync(x => x.Id == tenantId, cancellationToken);
        if (!tenantExists)
        {
            return null;
        }

        var defaultName = TenantConnectionNames.Default;

        // 一次取回精确名与默认名两行，回落在内存里判，避免两次往返
        var candidates = await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && (x.Name == normalized || x.Name == defaultName))
            .ToListAsync(cancellationToken);

        var hit = candidates.FirstOrDefault(x => x.Name == normalized)
                  ?? candidates.FirstOrDefault(x => x.Name == defaultName);

        // 命中就说明有；没命中才需要再问一次"到底有没有登记过任何连接"
        var hasAnyConnection = candidates.Count > 0
            || await dbContext.ConnectionsOfUndeletedTenants()
                .AnyAsync(x => x.TenantId == tenantId, cancellationToken);

        return new TenantConnectionLookupResult
        {
            TenantId = tenantId,
            HasAnyConnection = hasAnyConnection,
            Connection = hit is null ? null : ToConfiguration(hit)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantMigrationConnection>> GetListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = TenantConnectionNames.Normalize(name);
        var defaultName = TenantConnectionNames.Default;
        var dbContext = await _dbContextProvider.GetDbContextAsync(cancellationToken);

        var candidates = await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .Where(x => x.Name == normalized || x.Name == defaultName)
            .ToListAsync(cancellationToken);

        var resolved = candidates
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(x => x.Name == normalized) ?? group.First(x => x.Name == defaultName));

        // 登记过连接的租户全集：解析不出这个名字的必须让作业整体停下，而不是被静默跳过——
        // 跳过的库会停在旧结构上，下一次发版才炸
        var tenantIds = await dbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .Select(x => x.TenantId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var connections = new List<TenantMigrationConnection>(tenantIds.Count);
        foreach (var tenantId in tenantIds)
        {
            if (!resolved.TryGetValue(tenantId, out var record))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenantId}' has tenant-specific connections registered but none for " +
                    $"'{normalized}', and no default-named connection to fall back to. " +
                    "Fix its connection registrations before running the migrator.");
            }

            connections.Add(new TenantMigrationConnection(
                tenantId,
                record.Name,
                _protector.Unprotect(tenantId, record.Name, record.ProtectedConnectionString)));
        }

        return connections;
    }

    private TenantConnectionConfiguration ToConfiguration(TenantConnectionRecord record) => new()
    {
        TenantId = record.TenantId,
        Name = record.Name,
        ConnectionString = _protector.Unprotect(record.TenantId, record.Name, record.ProtectedConnectionString),
        Version = record.Version
    };
}
