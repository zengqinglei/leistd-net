using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Extensions;
using Microsoft.Extensions.Caching.Hybrid;

namespace Leistd.MultiTenancy.Caching;

/// <summary>按当前租户隔离键与标签，并在回源时恢复该租户。</summary>
/// <remarks>宿主使用 <c>host:</c> 分区。主体、工作单元等其他调用方上下文不随租户恢复。</remarks>
/// <param name="cache">宿主注册的 HybridCache。</param>
/// <param name="currentTenant">当前租户上下文。</param>
public sealed class TenantHybridCache(HybridCache cache, ICurrentTenant currentTenant) : HybridCache
{
    /// <inheritdoc />
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = currentTenant.Id;
        return cache.GetOrCreateAsync(
            CurrentTenantKeyExtensions.ScopeKey(tenantId, key),
            (Tenant: currentTenant, Id: tenantId, Name: currentTenant.Name, State: state, Factory: factory),
            static async (context, token) =>
            {
                using var tenantScope = context.Tenant.Change(context.Id, context.Name);
                return await context.Factory(context.State, token).ConfigureAwait(false);
            },
            options, ScopeKeys(tenantId, tags), cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        ReadOnlySpan<char> key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
        => GetOrCreateAsync(key.ToString(), state, factory, options, tags, cancellationToken);

    /// <inheritdoc />
    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = currentTenant.Id;
        return cache.SetAsync(
            CurrentTenantKeyExtensions.ScopeKey(tenantId, key), value, options, ScopeKeys(tenantId, tags), cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(currentTenant.ScopeKey(key), cancellationToken);

    /// <inheritdoc />
    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(ScopeKeys(currentTenant.Id, keys) ?? [], cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">标签为全局通配符 <c>*</c>。</exception>
    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        => cache.RemoveByTagAsync(ScopeInvalidationTag(currentTenant.Id, tag), cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">标签包含全局通配符 <c>*</c>。</exception>
    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var tenantId = currentTenant.Id;
        return cache.RemoveByTagAsync(tags?.Select(tag => ScopeInvalidationTag(tenantId, tag)).ToArray() ?? [], cancellationToken);
    }

    private static string[]? ScopeKeys(Guid? tenantId, IEnumerable<string>? keys)
        => keys?.Select(key => CurrentTenantKeyExtensions.ScopeKey(tenantId, key)).ToArray();

    private static string ScopeInvalidationTag(Guid? tenantId, string tag)
    {
        if (tag == "*")
        {
            throw new NotSupportedException("Tenant cache invalidation does not support the global wildcard tag.");
        }

        return CurrentTenantKeyExtensions.ScopeKey(tenantId, tag);
    }
}
