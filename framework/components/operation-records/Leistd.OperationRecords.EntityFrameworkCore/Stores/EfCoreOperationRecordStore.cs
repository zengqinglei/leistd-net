using Leistd.Data.Paging;
using Leistd.MultiTenancy.Context;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.OperationRecords.EntityFrameworkCore.Stores;

/// <summary>使用 EF Core 持久化操作记录，并提供历史读取。</summary>
/// <remarks>
/// <para>通过 <see cref="IDbContextProvider{TDbContext}"/> 参与工作单元：成功记录与业务数据同一事务提交。</para>
/// <para>租户隔离由 <c>IMultiTenant</c> 的全局查询过滤器承担，本类不带租户条件。</para>
/// </remarks>
/// <typeparam name="TDbContext">宿主 DbContext 类型（需包含 OperationRecord 配置）。</typeparam>
/// <param name="dbContextProvider">工作单元内的 DbContext 提供器。</param>
/// <param name="unitOfWorkManager">工作单元管理器，失败记录据它独立提交。</param>
/// <param name="currentTenant">当前租户上下文，失败记录据它切到记录所在的层。</param>
public class EfCoreOperationRecordStore<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    ICurrentTenant currentTenant) : IOperationRecordWriter, IOperationRecordReader
    where TDbContext : DbContext
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>成功记录就地保存：有环境工作单元时落进它的事务，由它决定提交还是回滚；没有时保存即生效。</para>
    /// <para>失败记录先切到记录所在的租户层，再在新开的工作单元里写入并提交；切租户须在开工作单元之前，
    /// 工作单元按开启时的租户绑定连接。</para>
    /// </remarks>
    public async Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Outcome == OperationRecordOutcome.Succeeded)
        {
            // 成功记录跟随调用方的事务，连的是当前租户的库，归属必须一致
            if (record.TenantId != currentTenant.Id)
            {
                throw new InvalidOperationException(
                    $"A succeeded operation record for tenant '{record.TenantId}' cannot be written "
                    + $"inside the context of tenant '{currentTenant.Id}'.");
            }

            await AddAsync(record, cancellationToken);
            return;
        }

        // 同层时不切：切换会丢掉当前上下文里的租户名
        using (record.TenantId == currentTenant.Id ? null : currentTenant.Change(record.TenantId))
        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await AddAsync(record, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    private async Task AddAsync(OperationRecordInfo record, CancellationToken cancellationToken)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        dbContext.Set<OperationRecord>().Add(OperationRecord.FromInfo(record));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<OperationRecordInfo>> GetPagedListAsync(
        OperationRecordFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filter.Scope);
        ArgumentNullException.ThrowIfNull(page);

        var scope = filter.Scope;

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var query = dbContext.Set<OperationRecord>().AsNoTracking();

        // 先物化成数组，EF 才翻译为 IN (...)；IReadOnlyCollection 在部分 Provider 上会退化为客户端求值
        if (filter.Actions is { Count: > 0 } actions)
        {
            var actionCodes = actions.ToArray();
            query = query.Where(x => actionCodes.Contains(x.Action));
        }

        if (filter.Outcome is { } requiredOutcome)
        {
            query = query.Where(x => x.Outcome == requiredOutcome);
        }

        // 可见性过滤在数据库里做，总数与分页才正确；租户维度由 IMultiTenant 的全局查询过滤器承担
        if (scope.IsRestricted)
        {
            var actorId = scope.ActorId;
            var actorTenantId = scope.ActorTenantId;
            var includesHostRecords = scope.IncludesHostRecords;
            // 先落成局部变量再进表达式树，避免把 scope 对象捕获进查询。
            // Actor 层对宿主（即能看 Host 层的读者）整层放行，否则宿主的 ActorId 为 null 会滤掉整层；
            // 对其余读者只放行标识与所属租户都相同的“本人”。
            query = query.Where(x =>
                x.Visibility == OperationVisibility.Tenant
                || (x.Visibility == OperationVisibility.Host && includesHostRecords)
                || (x.Visibility == OperationVisibility.Actor
                    && (includesHostRecords || (actorId != null && x.ActorId == actorId && x.ActorTenantId == actorTenantId))));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var trimmed = filter.Keyword.Trim();
            query = query.Where(x =>
                x.Action.Contains(trimmed)
                || x.TargetId.Contains(trimmed)
                || (x.TargetName != null && x.TargetName.Contains(trimmed))
                || (x.ActorName != null && x.ActorName.Contains(trimmed)));
        }

        // 两端闭区间：按天选择时调用方把上界取到当天 23:59:59.999
        if (filter.StartTime is { } startTime)
        {
            query = query.Where(x => x.CreationTime >= startTime);
        }

        if (filter.EndTime is { } endTime)
        {
            query = query.Where(x => x.CreationTime <= endTime);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        // 次级键让同一时刻的记录顺序确定，分页不重复不遗漏；不保证同一时刻内的先后（取决于 Provider 的 Guid 比较）
        var items = await query
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id)
            .Skip(page.Offset)
            .Take(page.Limit)
            .ToListAsync(cancellationToken);

        return new PagedResult<OperationRecordInfo>(totalCount, [.. items.Select(x => x.ToInfo())]);
    }
}
