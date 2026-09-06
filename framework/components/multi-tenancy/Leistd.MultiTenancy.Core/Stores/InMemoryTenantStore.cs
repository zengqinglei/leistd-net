using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.Stores;

/// <summary>
/// 从选项读取租户配置。
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
/// 配置 <see cref="InMemoryTenantStore"/> 的租户清单。
/// </summary>
public class InMemoryTenantStoreOptions
{
    /// <summary>
    /// 获取租户清单。
    /// </summary>
    public IList<TenantConfiguration> Tenants { get; } = [];
}
