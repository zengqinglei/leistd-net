using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予存储。
/// </summary>
/// <remarks>
/// 每个读取方法的数据库往返次数是常数（版本、授予、版本各一次——版本读两遍是为了确认
/// 期间无人写入，见 <c>ReadStableAsync</c>），与被检查的权限数量和主体所属角色数量无关，
/// 因此不存在按权限或按角色的 N+1。
/// </remarks>
public class EfCorePermissionGrantStore<TDbContext>(TDbContext dbContext) : IPermissionGrantStore
    where TDbContext : DbContext
{
    public async Task<PermissionGrantSet> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(providerKey))
            return PermissionGrantSet.Empty(providerName, providerKey);

        return await ReadStableAsync(
            providerName,
            providerKey,
            () => ReadRevisionsAsync([providerKey], cancellationToken),
            async () =>
            {
                var grants = await dbContext.Set<PermissionGrantRecord>()
                    .AsNoTracking()
                    .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
                    .Select(x => x.PermissionName)
                    .ToListAsync(cancellationToken);

                return (IReadOnlyList<string>)grants;
            },
            (grants, revisions) => new PermissionGrantSet(
                providerName,
                providerKey,
                grants,
                revisions.GetValueOrDefault(providerKey)));

        Task<Dictionary<string, long>> ReadRevisionsAsync(
            IReadOnlyCollection<string> keys,
            CancellationToken token)
            => dbContext.Set<AuthorizationRevisionRecord>()
                .AsNoTracking()
                .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
                .ToDictionaryAsync(x => x.ProviderKey, x => x.Version, StringComparer.Ordinal, token);
    }

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

        return await ReadStableAsync<Dictionary<string, IReadOnlyList<string>>, IReadOnlyList<PermissionGrantSet>>(
            providerName,
            string.Join(",", keys),
            () => dbContext.Set<AuthorizationRevisionRecord>()
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
            (grantsByKey, revisions) =>
            [
                .. keys.Select(key => new PermissionGrantSet(
                    providerName,
                    key,
                    grantsByKey.GetValueOrDefault(key) ?? [],
                    revisions.GetValueOrDefault(key)))
            ]);
    }

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

        return await ReadStableAsync(
            PermissionGrantProviderNames.User,
            userKey,
            () => dbContext.Set<AuthorizationRevisionRecord>()
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
            (grantsByKey, revisions) =>
            {
                PermissionGrantSet SetOf(string providerName, string providerKey)
                {
                    var key = providerName + "/" + providerKey;
                    return new PermissionGrantSet(
                        providerName,
                        providerKey,
                        grantsByKey.GetValueOrDefault(key) ?? [],
                        revisions.GetValueOrDefault(key));
                }

                return new SubjectPermissionGrants(
                    SetOf(PermissionGrantProviderNames.User, userKey),
                    [.. roleKeys.Select(roleKey => SetOf(PermissionGrantProviderNames.Role, roleKey))]);
            });
    }

    /// <summary>
    /// 在一致的版本快照下读取授予。
    /// </summary>
    /// <remarks>
    /// 授予集合与版本是两次查询，中间可能夹进一次写入，于是拼出"旧集合 + 新版本"——
    /// 拿它去保存会被判为无冲突，乐观并发形同虚设；下发给前端则会让旧权限带着新版本被缓存下来，
    /// 此后永不刷新。这里用"读版本 → 读数据 → 再读版本"确认期间无人写入，不一致就重来。
    ///
    /// 比起为此开 REPEATABLE READ 事务，多读一行带索引的版本记录代价可以忽略，
    /// 且不依赖具体数据库的隔离级别实现。
    /// </remarks>
    private static async Task<TResult> ReadStableAsync<TData, TResult>(
        string providerName,
        string providerKey,
        Func<Task<Dictionary<string, long>>> readRevisions,
        Func<Task<TData>> readData,
        Func<TData, Dictionary<string, long>, TResult> project)
    {
        // 同一主体连续 MaxAttempts 次写入才会耗尽重试，正常负载下第一次就稳定。
        const int MaxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var before = await readRevisions();
            var data = await readData();
            var after = await readRevisions();

            if (SameRevisions(before, after))
                return project(data, after);

            if (attempt >= MaxAttempts)
            {
                // 宁可失败也不返回撕裂的快照：调用方据此写入会静默丢失他人的修改。
                // 这是读取失败而不是保存冲突——此处没有调用方提交的期望版本，
                // 复用保存冲突异常会让宿主把它当成"旧页面撞车"报成 409。
                throw new UnstableGrantSnapshotException($"{providerName}/{providerKey}", attempt);
            }
        }

        static bool SameRevisions(Dictionary<string, long> before, Dictionary<string, long> after)
            => before.Count == after.Count
               && before.All(entry => after.TryGetValue(entry.Key, out var version) && version == entry.Value);
    }
}
