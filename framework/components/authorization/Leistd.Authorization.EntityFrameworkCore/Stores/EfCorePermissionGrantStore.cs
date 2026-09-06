using Microsoft.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Authorization.Permissions;
using Leistd.Authorization.Constants;
using Leistd.Authorization.EntityFrameworkCore.Entities;
using Leistd.Authorization.Exceptions;
using Leistd.Authorization.Abstractions;

namespace Leistd.Authorization.EntityFrameworkCore.Stores;

/// <summary>
/// EF Core 权限授予存储。
/// </summary>
/// <remarks>
/// 每个读取方法的数据库往返次数是常数，与权限数量、角色数量和批量主体数量都无关，不存在 N+1。
/// DbContext 一律经 <see cref="IDbContextProvider{TDbContext}"/> 获取而不直接注入 <typeparamref name="TDbContext"/>——
/// 只有它会设置 <c>DbContextCreationContext.Current</c>，直接注入会让独立库租户的读取静默落到默认连接上。
/// </remarks>
public class EfCorePermissionGrantStore<TDbContext>(IDbContextProvider<TDbContext> dbContextProvider)
    : IPermissionGrantStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<PermissionGrantSet> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(providerKey))
            return PermissionGrantSet.Empty(providerName, providerKey);

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        return await ReadStableAsync(
            providerName,
            providerKey,
            () => ReadVersionsAsync([providerKey], cancellationToken),
            async () =>
            {
                var grants = await dbContext.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
                    .Select(x => x.PermissionName)
                    .ToListAsync(cancellationToken);

                return (IReadOnlyList<string>)grants;
            },
            (grants, versions) => new PermissionGrantSet(
                providerName,
                providerKey,
                grants,
                versions.GetValueOrDefault(providerKey)));

        Task<Dictionary<string, long>> ReadVersionsAsync(
            IReadOnlyCollection<string> keys,
            CancellationToken token)
            => dbContext.Set<AuthorizationVersionRecord>()
                .AsNoTracking()
                .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
                .ToDictionaryAsync(x => x.ProviderKey, x => x.Version, StringComparer.Ordinal, token);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionGrantSet>> GetGrantsAsync(
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return [];

        var keys = providerKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
            return [];

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        return await ReadStableAsync<Dictionary<string, IReadOnlyList<string>>, IReadOnlyList<PermissionGrantSet>>(
            providerName,
            string.Join(",", keys),
            () => dbContext.Set<AuthorizationVersionRecord>()
                .AsNoTracking()
                .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
                .ToDictionaryAsync(x => x.ProviderKey, x => x.Version, StringComparer.Ordinal, cancellationToken),
            async () =>
            {
                var rows = await dbContext.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
                    .Select(x => new { x.ProviderKey, x.PermissionName })
                    .ToListAsync(cancellationToken);

                return rows
                    .GroupBy(x => x.ProviderKey, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)[.. group.Select(x => x.PermissionName)],
                        StringComparer.Ordinal);
            },
            (grantsByKey, versions) =>
            [
                .. keys.Select(key => new PermissionGrantSet(
                    providerName,
                    key,
                    grantsByKey.GetValueOrDefault(key) ?? [],
                    versions.GetValueOrDefault(key)))
            ]);
    }

    /// <inheritdoc />
    public async Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        var userKey = userId ?? string.Empty;
        var roleKeys = roleIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        return await ReadStableAsync(
            PermissionGrantProviderNames.User,
            userKey,
            () => dbContext.Set<AuthorizationVersionRecord>()
                .AsNoTracking()
                .Where(x => (x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userKey)
                            || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey)))
                .ToDictionaryAsync(x => x.ProviderName + "/" + x.ProviderKey, x => x.Version, StringComparer.Ordinal, cancellationToken),
            async () =>
            {
                var rows = await dbContext.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(x => (x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userKey)
                                || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey)))
                    .Select(x => new { x.ProviderName, x.ProviderKey, x.PermissionName })
                    .ToListAsync(cancellationToken);

                return rows
                    .GroupBy(x => x.ProviderName + "/" + x.ProviderKey, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)[.. group.Select(x => x.PermissionName)],
                        StringComparer.Ordinal);
            },
            (grantsByKey, versions) =>
            {
                PermissionGrantSet SetOf(string providerName, string providerKey)
                {
                    var key = providerName + "/" + providerKey;
                    return new PermissionGrantSet(
                        providerName,
                        providerKey,
                        grantsByKey.GetValueOrDefault(key) ?? [],
                        versions.GetValueOrDefault(key));
                }

                return new SubjectPermissionGrants(
                    SetOf(PermissionGrantProviderNames.User, userKey),
                    [.. roleKeys.Select(roleKey => SetOf(PermissionGrantProviderNames.Role, roleKey))]);
            });
    }

    // 在一致的版本快照下读取授予。
    // 授予集合与版本是两次查询，中间可能夹进一次写入，于是拼出"旧集合 + 新版本"——
    // 拿它去保存会被判为无冲突，乐观并发形同虚设；下发给前端则会让旧权限带着新版本被缓存下来，
    // 此后永不刷新。这里用"读版本 → 读数据 → 再读版本"确认期间无人写入，不一致就重来。
    // 比起为此开 REPEATABLE READ 事务，多读一行带索引的版本记录代价可以忽略，
    // 且不依赖具体数据库的隔离级别实现。
    private static async Task<TResult> ReadStableAsync<TData, TResult>(
        string providerName,
        string providerKey,
        Func<Task<Dictionary<string, long>>> readVersions,
        Func<Task<TData>> readData,
        Func<TData, Dictionary<string, long>, TResult> project)
    {
        // 同一主体连续 MaxAttempts 次写入才会耗尽重试，正常负载下第一次就稳定。
        const int MaxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var before = await readVersions();
            var data = await readData();
            var after = await readVersions();

            if (SameVersions(before, after))
                return project(data, after);

            if (attempt >= MaxAttempts)
            {
                // 宁可失败也不返回撕裂的快照：调用方据此写入会静默丢失他人的修改。
                // 这是读取失败而不是保存冲突——此处没有调用方提交的期望版本，
                // 复用保存冲突异常会让宿主把它当成"旧页面撞车"报成 409。
                throw new UnstableGrantSnapshotException($"{providerName}/{providerKey}", attempt);
            }
        }

        static bool SameVersions(Dictionary<string, long> before, Dictionary<string, long> after)
            => before.Count == after.Count
               && before.All(entry => after.TryGetValue(entry.Key, out var version) && version == entry.Value);
    }
}
