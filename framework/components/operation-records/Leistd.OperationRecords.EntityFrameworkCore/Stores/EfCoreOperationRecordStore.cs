using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.OperationRecords.EntityFrameworkCore.Stores;

/// <summary>
/// 使用 EF Core 持久化操作记录。
/// </summary>
/// <remarks>
/// 通过 <see cref="IDbContextProvider{TDbContext}"/> 获取当前边界的上下文与连接，参与工作单元。
/// 租户隔离由 <c>IMultiTenant</c> 的全局查询过滤器承担，本类不带租户条件。
/// </remarks>
/// <typeparam name="TDbContext">宿主 DbContext 类型（需包含 OperationRecord 配置）。</typeparam>
/// <param name="dbContextProvider">工作单元内的 DbContext 提供器。</param>
public class EfCoreOperationRecordStore<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider) : IOperationRecordStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>与 <c>EfCoreSettingStore</c>、<c>EfCoreNotificationStore</c> 同型：就地保存。
    /// 这不等于"立即提交"——有环境工作单元时写入落进它的事务，由它决定提交还是回滚；
    /// 没有工作单元时 <see cref="IDbContextProvider{TDbContext}"/> 给的是当前作用域新建的上下文，
    /// 保存即生效。</para>
    /// <para><b>本存储不自行开启事务。</b>一条记录要不要扛过外层回滚，取决于调用方在哪里记——
    /// 那只有宿主知道。组件替它开第二个事务会与外层未提交的写入互相加锁，
    /// 这正是工作单元组件对 <c>requiresNew</c> 的告诫。</para>
    /// </remarks>
    public async Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        dbContext.Set<OperationRecord>().Add(OperationRecord.FromInfo(record));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationRecordPage> GetPagedListAsync(
        string? keyword,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take,
        OperationRecordVisibilityScope scope = default,
        IReadOnlyCollection<string>? actions = null,
        OperationRecordOutcome? outcome = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var query = dbContext.Set<OperationRecord>().AsNoTracking();

        // 动作码集合先物化成数组：EF 把它翻成 IN (...)，而传 IReadOnlyCollection 进表达式树
        // 在部分 Provider 上会退化成客户端求值——那会把整表拉进内存，且总数算错。
        if (actions is { Count: > 0 })
        {
            var actionCodes = actions.ToArray();
            query = query.Where(x => actionCodes.Contains(x.Action));
        }

        if (outcome is { } requiredOutcome)
        {
            query = query.Where(x => x.Outcome == requiredOutcome);
        }

        // 可见性过滤在数据库里做，不是查出来再筛：内存筛会让总数与当页双双算错，分页直接失效。
        // 租户维度不在这里——那由 IMultiTenant 的全局查询过滤器承担（谓词 TenantId == CurrentTenantId，
        // 宿主视角下即 TenantId == null）。本段只在同一层内部再分一次"这条给不给看"。
        if (scope.IsRestricted)
        {
            var actorId = scope.ActorId;
            var includesHostRecords = scope.IncludesHostRecords;
            // 两个布尔都先落成局部变量再进表达式树：直接写 scope.XXX 会把整个结构体
            // 捕获进查询，EF 需要把成员访问翻译成 SQL，翻不动时报的是很难对上号的运行期错。
            // Actor 层对宿主整层放行，对其余读者只放行"本人"。
            //
            // **能看 Host 层的读者就是宿主**，这里复用 includesHostRecords 表达这件事，
            // 而不是再加一个恒等于它的布尔——那种冗余状态迟早会与它漂移。
            //
            // 少了 includesHostRecords 这一支会怎样：宿主的 ActorId 恒为 null，
            // `actorId != null` 恒假，于是 Actor 层被整层滤掉。症状是宿主管理员
            // **看不到自己的登录记录**，而界面只会显示"暂无操作记录"——
            // 既不报错也不提示，最难联想到是可见性判定的问题。
            query = query.Where(x =>
                x.Visibility == OperationVisibility.Tenant
                || (x.Visibility == OperationVisibility.Host && includesHostRecords)
                || (x.Visibility == OperationVisibility.Actor
                    && (includesHostRecords || (actorId != null && x.ActorId == actorId))));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(x =>
                x.Action.Contains(trimmed)
                || x.TargetId.Contains(trimmed)
                || (x.ActorName != null && x.ActorName.Contains(trimmed)));
        }

        // 两端都是闭区间：调用方给的是"从这一刻到那一刻"，而不是半开区间。
        // 界面上选到某一天时，调用方应把上界取到那天的 23:59:59.999，否则当天的记录会整天看不见。
        if (startTime.HasValue)
        {
            query = query.Where(x => x.CreationTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.CreationTime <= endTime.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        // 同一毫秒内的多条记录时间相同，只按时间排序会让分页出现重复或遗漏。次级键消除这种不确定。
        // 它保证的是"顺序确定"，不是"同一时刻内更新的在前"：后者取决于 Provider 怎么比较 Guid
        // （PostgreSQL 的 uuid 按网络字节序，v7 时间序成立；SQLite 存 BLOB 按 .NET 字节布局比较，
        // 前三段小端，时间序不成立）。分页要的是前者。
        var items = await query
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new OperationRecordPage(totalCount, [.. items.Select(x => x.ToInfo())]);
    }
}
