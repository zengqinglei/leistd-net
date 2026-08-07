using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予管理器。
/// </summary>
/// <remarks>
/// 所有写入都经过同一条归一化流水线，因此单条授予、批量替换与种子数据得到一致的结果：
/// <list type="number">
/// <item>校验权限已定义且启用，未定义的权限名直接拒绝写入（抛 <see cref="UndefinedPermissionException"/>），
/// 避免拼错或残留的权限进入存储。这是该规则的唯一执行点，调用方不必也不应重复判断；</item>
/// <item>显式拒绝向下传播：被拒绝权限的全部子孙授予被移除，子孙因此回落为"未授予"，运行时同样拒绝；</item>
/// <item>允许向上补齐：被允许权限的全部祖先补为允许，使运行时保持扁平查找而无需回溯定义树。</item>
/// </list>
/// 每个公开写方法以一次 <c>SaveChangesAsync</c> 提交，批量替换因此是单事务操作。
/// 授权版本是并发令牌：并发写入中只有一个能成功，落败方得到
/// <see cref="PermissionGrantConcurrencyException"/> 而不是静默覆盖对方的修改。
/// </remarks>
public class EfCorePermissionGrantManager<TDbContext>(
    TDbContext dbContext,
    IPermissionDefinitionManager permissionDefinitionManager) : IPermissionGrantManager
    where TDbContext : DbContext
{
    public async Task GrantAsync(
        string permissionName,
        string providerName,
        string providerKey,
        PermissionGrantEffect effect = PermissionGrantEffect.Granted,
        CancellationToken cancellationToken = default)
    {
        ValidatePermissions([permissionName]);

        var desired = await ReadDesiredAsync(providerName, providerKey, cancellationToken);
        desired[permissionName] = effect;

        await ApplyAsync(providerName, providerKey, desired, expectedRevision: null, cancellationToken);
    }

    public async Task RevokeAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var desired = await ReadDesiredAsync(providerName, providerKey, cancellationToken);
        desired.Remove(permissionName);

        // 级联：父权限被撤销后，其子孙不再具备前置条件，一并清理。
        foreach (var descendant in permissionDefinitionManager.GetDescendantNames(permissionName))
        {
            desired.Remove(descendant);
        }

        await ApplyAsync(providerName, providerKey, desired, expectedRevision: null, cancellationToken);
    }

    public async Task<long> ReplaceGrantsAsync(
        string providerName,
        string providerKey,
        IReadOnlyCollection<PermissionGrant> grants,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePermissions(grants.Select(x => x.PermissionName));

        var desired = new Dictionary<string, PermissionGrantEffect>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            // 同一权限重复出现时拒绝优先。
            if (desired.TryGetValue(grant.PermissionName, out var existing) &&
                existing == PermissionGrantEffect.Prohibited)
            {
                continue;
            }

            desired[grant.PermissionName] = grant.Effect;
        }

        return await ApplyAsync(providerName, providerKey, desired, expectedRevision, cancellationToken);
    }

    private async Task<Dictionary<string, PermissionGrantEffect>> ReadDesiredAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .Select(x => new { x.PermissionName, x.Effect })
            .ToListAsync(cancellationToken);

        return records.ToDictionary(x => x.PermissionName, x => x.Effect, StringComparer.Ordinal);
    }

    /// <summary>
    /// 归一化目标授予集合：先向下传播拒绝，再向上补齐允许。
    /// </summary>
    private Dictionary<string, PermissionGrantEffect> Normalize(
        Dictionary<string, PermissionGrantEffect> desired)
    {
        // 1) 拒绝向下传播：移除被拒绝权限的全部子孙，使其回落为未授予。
        var prohibited = desired
            .Where(x => x.Value == PermissionGrantEffect.Prohibited)
            .Select(x => x.Key)
            .ToList();

        foreach (var name in prohibited)
        {
            foreach (var descendant in permissionDefinitionManager.GetDescendantNames(name))
            {
                desired.Remove(descendant);
            }
        }

        // 2) 允许向上补齐：此时任何被允许的权限都不会有被拒绝的祖先。
        var granted = desired
            .Where(x => x.Value == PermissionGrantEffect.Granted)
            .Select(x => x.Key)
            .ToList();

        foreach (var name in granted)
        {
            foreach (var ancestor in permissionDefinitionManager.GetAncestorNames(name))
            {
                if (!desired.ContainsKey(ancestor))
                {
                    desired[ancestor] = PermissionGrantEffect.Granted;
                }
            }
        }

        return desired;
    }

    private async Task<long> ApplyAsync(
        string providerName,
        string providerKey,
        Dictionary<string, PermissionGrantEffect> desired,
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

        var changed = false;
        var grantSet = dbContext.Set<PermissionGrantRecord>();

        foreach (var record in existing)
        {
            if (!target.TryGetValue(record.PermissionName, out var effect))
            {
                grantSet.Remove(record);
                changed = true;
            }
            else if (record.Effect != effect)
            {
                record.Effect = effect;
                changed = true;
            }
        }

        var existingNames = existing
            .Select(x => x.PermissionName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (permissionName, effect) in target)
        {
            if (existingNames.Contains(permissionName))
                continue;

            grantSet.Add(new PermissionGrantRecord
            {
                PermissionName = permissionName,
                ProviderName = providerName,
                ProviderKey = providerKey,
                Effect = effect
            });
            changed = true;
        }

        if (!changed)
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
        catch (DbUpdateConcurrencyException)
        {
            // 更新路径：并发令牌命中 0 行，说明另一个事务在本次读版本之后完成了写入。
            throw Conflict();
        }
        catch (DbUpdateException) when (isFirstWrite)
        {
            // 插入路径没有可比对的版本行，冲突表现为唯一索引违例，而唯一索引违例也可能来自
            // 其他约束。因此不猜测：清掉跟踪状态重新读一次，确认版本行确实已被他人建立才转成
            // 业务异常，否则原样抛出，避免把真实的写入错误伪装成 409。
            dbContext.ChangeTracker.Clear();

            var created = await dbContext.Set<AuthorizationRevisionRecord>()
                .AsNoTracking()
                .AnyAsync(
                    x => x.ProviderName == providerName && x.ProviderKey == providerKey,
                    cancellationToken);

            if (!created)
                throw;

            throw Conflict();
        }

        return revision.Version;

        PermissionGrantConcurrencyException Conflict()
            => new(
                providerName,
                providerKey,
                expectedRevision ?? currentRevision,
                currentRevision);
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
}
