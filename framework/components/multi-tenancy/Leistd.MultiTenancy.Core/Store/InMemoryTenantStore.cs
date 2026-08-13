using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 配置型租户存储：租户清单来自 Options（配置文件或代码），适合不持有租户注册表的资源服务与测试
/// </summary>
public class InMemoryTenantStore(IOptions<InMemoryTenantStoreOptions> options) : ITenantStore
{
    /// <inheritdoc />
    public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(options.Value.Tenants.FirstOrDefault(t => t.Id == id));

    /// <inheritdoc />
    public Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
        => Task.FromResult(options.Value.Tenants.FirstOrDefault(
            t => string.Equals(t.NormalizedName, normalizedName, StringComparison.Ordinal)));
}

/// <summary>
/// <see cref="InMemoryTenantStore"/> 的租户清单
/// </summary>
public class InMemoryTenantStoreOptions
{
    /// <summary>
    /// 租户清单
    /// </summary>
    public IList<TenantConfiguration> Tenants { get; } = [];
}
