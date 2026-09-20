using System.Reflection;
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace Leistd.OperationRecords.EntityFrameworkCore.Retention;

// 逐库执行：IgnoreQueryFilters 只能放开同一个库里的租户，独立库租户的记录在它自己的库里。
// 采用 AddRange + RemoveRange 而非 ExecuteDelete：兼容所有 EF 提供程序（含内存库），并复用 DbContext 上的拦截器。
internal sealed class OperationRecordArchiveService<TDbContext>(
    ITenantDatabaseRunner databaseRunner,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<TDbContext> dbContextProvider,
    IClock clock) : IOperationRecordArchiveService
    where TDbContext : DbContext
{
    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task<OperationRecordArchiveResult> ArchiveOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var archived = 0;
        // 停用租户的库照样要归档：保留期是合规义务，不随租户停用消失
        var result = await databaseRunner.ForEachDatabaseAsync(ConnectionStringName, activeOnly: false, async (_, ct) =>
        {
            archived += await ArchiveCurrentDatabaseAsync(cutoffUtc, batchSize, ct);
        }, cancellationToken);

        return new OperationRecordArchiveResult(
            archived, result.Databases, result.FailedDatabases.Count, result.UnresolvedTenants.Count);
    }

    private async Task<int> ArchiveCurrentDatabaseAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 每批一个工作单元：事务有界，跟踪的实体随工作单元释放
            using var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
            var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

            // IgnoreQueryFilters 是必需的：租户过滤器只放行当前上下文那一个租户，同库其余租户的记录不加就永远留着
            var batch = await dbContext.Set<OperationRecord>()
                .IgnoreQueryFilters()
                .Where(record => record.CreationTime < cutoffUtc)
                .OrderBy(record => record.CreationTime)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                return total;
            }

            var archivedTime = clock.Now;
            dbContext.Set<OperationRecordArchive>().AddRange(batch.Select(record => OperationRecordArchive.From(record, archivedTime)));
            dbContext.Set<OperationRecord>().RemoveRange(batch);

            // 一次提交覆盖"写归档 + 删原表"：分两次会出现"已删除但没归档"的窗口，那是不可逆的数据丢失
            await unitOfWork.CompleteAsync(cancellationToken);

            total += batch.Count;
            if (batch.Count < batchSize)
            {
                return total;
            }
        }
    }
}
