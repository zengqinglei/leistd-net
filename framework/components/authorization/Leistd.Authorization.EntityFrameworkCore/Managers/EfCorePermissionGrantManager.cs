using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予管理器。
/// </summary>
/// <remarks>
/// 所有写入都经过同一条归一化流水线，因此单条授予、批量替换与种子数据得到一致的结果：
/// <list type="number">
/// <item>校验权限已定义且启用，未定义的权限名直接拒绝写入（抛 <see cref="UndefinedPermissionException"/>），
/// 避免拼错或残留的权限进入存储。这是该规则的唯一执行点，调用方不必也不应重复判断；</item>
/// <item>向上补齐：被授予权限的全部祖先一并授予，使运行时保持扁平查找而无需回溯定义树。</item>
/// </list>
/// 每个公开写方法以一次 <c>SaveChangesAsync</c> 提交，批量替换因此是单事务操作。
/// 授权版本是并发令牌：并发写入中只有一个能成功，落败方得到
/// <see cref="PermissionGrantConcurrencyException"/> 而不是静默覆盖对方的修改。
/// </remarks>
public class EfCorePermissionGrantManager<TDbContext>(
    TDbContext dbContext,
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore) : IPermissionGrantManager
    where TDbContext : DbContext
{
    public async Task<int> RemoveProviderAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(providerName, providerKey);

        // 授予行与版本行必须一起消失。撤销到空集合会保留并递增版本，那是给"还有人在编辑"准备的；
        // 主体已经永久删除时留着版本行只会变成永久孤儿。
        var grants = await dbContext.Set<PermissionGrantRecord>()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);

        var revisions = await dbContext.Set<AuthorizationRevisionRecord>()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0 && revisions.Count == 0)
            return 0;

        dbContext.RemoveRange(grants);
        dbContext.RemoveRange(revisions);
        await dbContext.SaveChangesAsync(cancellationToken);

        return grants.Count;
    }

    public async Task GrantAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(providerName, providerKey);
        ValidatePermissions([permissionName]);

        await MutateAsync(
            providerName,
            providerKey,
            desired => desired.Add(permissionName),
            cancellationToken);
    }

    public async Task RevokeAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(providerName, providerKey);

        await MutateAsync(
            providerName,
            providerKey,
            desired =>
            {
                desired.Remove(permissionName);

                // 级联：父权限被撤销后，其子孙不再具备前置条件，一并清理。
                foreach (var descendant in permissionDefinitionManager.GetDescendantNames(permissionName))
                {
                    desired.Remove(descendant);
                }
            },
            cancellationToken);
    }

    public async Task<long> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        IReadOnlyCollection<string> permissionNames,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProvider(providerName, providerKey);
        ValidatePermissions(permissionNames);

        var desired = new HashSet<string>(permissionNames, StringComparer.Ordinal);

        return await ApplyAsync(providerName, providerKey, desired, expectedRevision, cancellationToken);
    }

    /// <summary>
    /// 以"读当前集合 → 就地改一处 → 带版本写回"的方式修改单条授予，冲突时重试。
    /// </summary>
    /// <remarks>
    /// 不带版本写回会静默覆盖：读集合与读版本之间夹进另一次写入时，本次会拿旧集合配新版本，
    /// 把对方刚加的权限删掉，而且不触发任何并发异常。
    ///
    /// 冲突在内部重试而不是抛给调用方：这两个 API 的语义是幂等的点操作（加上 X / 去掉 X），
    /// 与期间别人改了什么无关，让调用方为此写重试是把实现细节推出去。
    /// 全量替换不同——那是从界面提交的整份集合，内部重试等于把丢失更新原样请回来，
    /// 因此 <see cref="ReplaceGrantsAsync"/> 仍把冲突抛出。
    /// </remarks>
    private async Task MutateAsync(
        string providerName,
        string providerKey,
        Action<HashSet<string>> mutate,
        CancellationToken cancellationToken)
    {
        const int MaxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var snapshot = await permissionGrantStore.GetGrantsAsync(providerName, providerKey, cancellationToken);
            var desired = new HashSet<string>(snapshot.PermissionNames, StringComparer.Ordinal);
            mutate(desired);

            try
            {
                await ApplyAsync(providerName, providerKey, desired, snapshot.Revision, cancellationToken);
                return;
            }
            catch (PermissionGrantConcurrencyException) when (attempt < MaxAttempts)
            {
                // 期间有人写入：拿最新集合重做这一处改动。
            }
        }
    }

    /// <summary>
    /// 归一化目标授予集合：向上补齐祖先。
    /// </summary>
    /// <remarks>
    /// 父权限是子权限的前置条件（能创建用户必然要能查看用户），补齐后运行时只需一次扁平查找。
    /// </remarks>
    private HashSet<string> Normalize(HashSet<string> desired)
    {
        foreach (var name in desired.ToList())
        {
            desired.UnionWith(permissionDefinitionManager.GetAncestorNames(name));
        }

        return desired;
    }

    private async Task<long> ApplyAsync(
        string providerName,
        string providerKey,
        HashSet<string> desired,
        long? expectedRevision,
        CancellationToken cancellationToken)
    {
        var target = Normalize(desired);

        var revision = await dbContext.Set<AuthorizationRevisionRecord>()
            .FirstOrDefaultAsync(
                x => x.ProviderName == providerName && x.ProviderKey == providerKey,
                cancellationToken);

        var currentRevision = revision?.Version ?? 0;
        if (expectedRevision.HasValue && expectedRevision.Value != currentRevision)
        {
            throw new PermissionGrantConcurrencyException(
                providerName,
                providerKey,
                expectedRevision.Value,
                currentRevision);
        }

        var existing = await dbContext.Set<PermissionGrantRecord>()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);

        var grantSet = dbContext.Set<PermissionGrantRecord>();

        // 记下本次改动的条目：写入失败时要精确还原它们的跟踪状态。DbContext 是宿主的工作单元，
        // 失败后把我们的待写实体留在里面，调用方接住异常继续 SaveChangesAsync 就会把落败方的
        // 授予写进库，而版本号并未递增——后续乐观并发从此形同虚设。
        var touched = new List<EntityEntry<PermissionGrantRecord>>();

        foreach (var record in existing.Where(record => !target.Contains(record.PermissionName)))
        {
            touched.Add(grantSet.Remove(record));
        }

        var existingNames = existing
            .Select(x => x.PermissionName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permissionName in target.Where(name => !existingNames.Contains(name)))
        {
            touched.Add(grantSet.Add(new PermissionGrantRecord
            {
                PermissionName = permissionName,
                ProviderName = providerName,
                ProviderKey = providerKey
            }));
        }

        if (touched.Count == 0)
            return currentRevision;

        var isFirstWrite = revision == null;
        if (revision == null)
        {
            revision = new AuthorizationRevisionRecord
            {
                ProviderName = providerName,
                ProviderKey = providerKey,
                Version = 1
            };
            await dbContext.Set<AuthorizationRevisionRecord>().AddAsync(revision, cancellationToken);
        }
        else
        {
            revision.Version += 1;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (InvolvesThisSubject(ex))
        {
            // 更新路径：并发令牌命中 0 行，说明另一个事务在本次读版本之后完成了写入。
            //
            // 只接管失败条目里确实有本主体授权行的情况：DbContext 是宿主的工作单元，
            // 同一次 SaveChanges 里业务实体的并发令牌也可能失败，无条件接管会把
            // "订单被别人改了"报成"权限被别的管理员改了"，真正的问题就此消失。
            // 不属于我们的异常原样穿过，连现场都不收拾——那次保存整体失败，善后是宿主的事。
            DiscardPendingChanges();
            throw await ConflictAsync();
        }
        catch (DbUpdateException) when (isFirstWrite)
        {
            // 插入路径没有可比对的版本行，冲突表现为唯一索引违例，而唯一索引违例也可能来自
            // 其他约束。因此不猜测：重新读一次，确认版本行确实已被他人建立才转成业务异常，
            // 否则原样抛出，避免把真实的写入错误伪装成 409。
            DiscardPendingChanges();

            var conflict = await ConflictAsync();
            if (conflict.ActualRevision == 0)
                throw;

            throw conflict;
        }

        return revision.Version;

        // 失败条目里是否有本主体的授权行。EF 只把并发令牌校验失败的条目放进 Entries，
        // 因此这等价于"这次冲突是不是我们造成的"。
        bool InvolvesThisSubject(DbUpdateConcurrencyException exception)
            => exception.Entries.Any(entry => entry.Entity switch
            {
                AuthorizationRevisionRecord record
                    => record.ProviderName == providerName && record.ProviderKey == providerKey,
                PermissionGrantRecord record
                    => record.ProviderName == providerName && record.ProviderKey == providerKey,
                _ => false
            });

        // 逐条还原本次动过的条目，绝不用 ChangeTracker.Clear()——那会连带解除与授权无关的实体，
        // 把宿主工作单元里别人的待写改动一并丢掉。
        void DiscardPendingChanges()
        {
            foreach (var entry in touched)
            {
                entry.State = entry.State switch
                {
                    EntityState.Added => EntityState.Detached,
                    EntityState.Deleted => EntityState.Unchanged,
                    _ => entry.State
                };
            }

            // 版本行一律脱离跟踪而不是回滚版本号：赢家已经把库里的值推高，
            // 留一个版本号过时的实体在跟踪器里，下一次 SaveChanges 只会写出一个错误的版本。
            dbContext.Entry(revision).State = EntityState.Detached;
        }

        // 实际版本必须重新读：currentRevision 是本次写入前的值，赢家已经把它推高了，
        // 直接回填会让调用方拿到一个存储中并不存在的版本号。
        async Task<PermissionGrantConcurrencyException> ConflictAsync()
        {
            var actualRevision = await dbContext.Set<AuthorizationRevisionRecord>()
                .AsNoTracking()
                .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
                .Select(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);

            return new PermissionGrantConcurrencyException(
                providerName,
                providerKey,
                expectedRevision ?? currentRevision,
                actualRevision);
        }
    }

    /// <summary>
    /// 校验权限已定义且启用。这是"未定义权限不落库"的唯一执行点，覆盖全部调用路径。
    /// </summary>
    private void ValidatePermissions(IEnumerable<string> permissionNames)
    {
        var unknown = permissionNames
            .Where(name => !permissionDefinitionManager.IsEffectivelyEnabled(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unknown.Count > 0)
            throw new UndefinedPermissionException(unknown);
    }

    /// <summary>
    /// 校验授予主体标识非空。
    /// </summary>
    /// <remarks>
    /// 这是框架公共 API 的边界守卫，不是对上层 DTO 校验的重复：种子数据、后台任务与下游项目
    /// 都可以直接调用本管理器，空白标识一旦落库就会产生谁也看不到、谁也删不掉的孤儿授予。
    /// </remarks>
    private static void ValidateProvider(string providerName, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
    }
}
