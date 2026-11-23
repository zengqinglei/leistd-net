using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Extensions;
using Leistd.Settings.Checks;
using Leistd.Settings.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Settings.EntityFrameworkCore.Stores;

/// <summary>使用 EF Core 持久化设置测试的最近结果。</summary>
/// <remarks>经 <see cref="IDbContextProvider{TDbContext}"/> 参与当前工作单元；租户隔离由查询过滤器承担，<c>ScopeKey</c> 保证唯一。</remarks>
/// <typeparam name="TDbContext">宿主 DbContext 类型（需包含 SettingCheckRecord 配置）。</typeparam>
/// <param name="dbContextProvider">工作单元内的 DbContext 提供器。</param>
/// <param name="currentTenant">当前租户上下文，用于派生 <c>ScopeKey</c>。</param>
public class EfCoreSettingCheckStore<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    ICurrentTenant currentTenant) : ISettingCheckStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, SettingCheckResult>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var scopeKey = ScopeKey();
        return await dbContext.Set<SettingCheckRecord>()
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey)
            .ToDictionaryAsync(
                x => x.Name,
                x => new SettingCheckResult(
                    x.Passed,
                    x.FailureCode,
                    DateTime.SpecifyKind(x.CheckTime, DateTimeKind.Utc),
                    x.CheckerId,
                    x.CheckerName,
                    x.IsStale),
                StringComparer.Ordinal,
                cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>两次测试同时首插时，后者撞唯一索引后改为覆盖赢家那一行。</remarks>
    public async Task SaveAsync(string settingName, SettingCheckResult result, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        ArgumentNullException.ThrowIfNull(result);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var scopeKey = ScopeKey();
        var set = dbContext.Set<SettingCheckRecord>();

        var record = await set.FirstOrDefaultAsync(x => x.ScopeKey == scopeKey && x.Name == settingName, cancellationToken);
        var added = record is null;
        record ??= set.Add(new SettingCheckRecord { ScopeKey = scopeKey, Name = settingName }).Entity;
        Apply(record, result);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (added && exception.Entries.Any(entry => ReferenceEquals(entry.Entity, record)))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var winner = await set.FirstOrDefaultAsync(x => x.ScopeKey == scopeKey && x.Name == settingName, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            Apply(winner, result);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingNames);
        if (settingNames.Count == 0)
        {
            return;
        }

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var scopeKey = ScopeKey();
        // 加载后改：兼容所有 EF 提供程序（含内存库），与设置值的写入在同一次保存里提交
        var records = await dbContext.Set<SettingCheckRecord>()
            .Where(x => x.ScopeKey == scopeKey && settingNames.Contains(x.Name) && !x.IsStale)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return;
        }

        foreach (var record in records)
        {
            record.IsStale = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(SettingCheckRecord record, SettingCheckResult result)
    {
        record.Passed = result.Passed;
        record.FailureCode = result.FailureCode;
        record.CheckTime = result.Time;
        record.CheckerId = result.CheckerId;
        record.CheckerName = result.CheckerName;
        record.IsStale = false;
    }

    // 与 EfCoreSettingStore 的租户级同一写法：宿主为 host:t
    private string ScopeKey() => currentTenant.ScopeKey("t");
}
