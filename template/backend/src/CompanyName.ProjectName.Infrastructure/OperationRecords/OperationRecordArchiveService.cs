using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.Timing;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.OperationRecords;

/// <summary>
/// 基于 EF Core 的操作记录归档：分批把到期记录搬入归档表。
/// </summary>
/// <remarks>
/// 采用 <c>AddRange + RemoveRange + SaveChangesAsync</c>（而非 <c>ExecuteDeleteAsync</c>），
/// 与 <c>NotificationCleanupService</c> 同一理由：兼容关系型与 InMemory 等所有 EF 提供程序
/// （模板的运行期冒烟正是跑内存库），并复用 DbContext 上的审计/事件拦截器。
/// 批量删除在内存库上根本不可用，用了会让冒烟阶段直接失败。
/// </remarks>
public class OperationRecordArchiveService(MyProjectDbContext dbContext, IClock clock)
    : IOperationRecordArchiveService
{
    /// <inheritdoc />
    public async Task<int> ArchiveOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // **IgnoreQueryFilters 是必需的，不是优化。**
            // OperationRecord 实现 IMultiTenant，带着租户全局过滤器；而本服务跑在
            // **无租户上下文**里，此时过滤器的语义是"只放行宿主自己的行"，不是"放行所有租户"。
            // 不加这一句，归档只会搬走宿主那部分，所有租户的记录永远留着——
            // 而且不报错，日志照样显示"归档成功 N 条"。
            var batch = await dbContext
                .Set<OperationRecord>()
                .IgnoreQueryFilters()
                .Where(record => record.CreationTime < cutoffUtc)
                .OrderBy(record => record.CreationTime)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            var archivedTime = clock.Now;
            dbContext.Set<OperationRecordArchive>()
                .AddRange(batch.Select(record => ToArchive(record, archivedTime)));
            dbContext.Set<OperationRecord>().RemoveRange(batch);

            // 一次 SaveChanges 覆盖"写归档 + 删原表"：两者必须同生共死。
            // 分两次保存会出现"已删除但没归档"的窗口，而那是不可逆的数据丢失。
            await dbContext.SaveChangesAsync(cancellationToken);

            total += batch.Count;

            // 清掉跟踪：不清的话每批的实体都会累积在 ChangeTracker 里，
            // 跑到第几十批时这个"不起眼的后台任务"会成为进程里最大的内存峰值。
            dbContext.ChangeTracker.Clear();

            if (batch.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }

    private static OperationRecordArchive ToArchive(OperationRecord record, DateTime archivedTime) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Action = record.Action,
        TargetId = record.TargetId,
        AuthorizationBasis = record.AuthorizationBasis,
        Outcome = record.Outcome,
        CreationTime = record.CreationTime,
        ArchivedTime = archivedTime,
        ActorId = record.ActorId,
        ActorName = record.ActorName,
        ImpersonatorId = record.ImpersonatorId,
        ImpersonatorName = record.ImpersonatorName,
        CorrelationId = record.CorrelationId,
        TargetName = record.TargetName,
        Visibility = record.Visibility,
        FailureCode = record.FailureCode,
        FailureData = record.FailureData,
        FailureDetail = record.FailureDetail,
    };
}
