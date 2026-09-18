using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Data.Constants;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.OperationRecords;

/// <summary>
/// 基于 EF Core 的操作记录归档：按物理库逐个进入，分批把到期记录搬入归档表。
/// </summary>
/// <remarks>
/// <para><b>逐库执行。</b>无租户上下文里的 <c>IgnoreQueryFilters()</c> 只能放开同一个库里的租户，
/// 独立库租户的记录在它自己的库里。只连宿主库的话，这些库永远不会被归档，日志却照样显示成功。
/// 每个库单独隔离失败：一个库连不上，不该让其余的库也跳过。</para>
/// <para>采用 <c>AddRange + RemoveRange + SaveChangesAsync</c>（而非 <c>ExecuteDeleteAsync</c>），
/// 与 <c>NotificationCleanupService</c> 同一理由：兼容关系型与 InMemory 等所有 EF 提供程序
/// （模板的运行期冒烟正是跑内存库），并复用 DbContext 上的审计/事件拦截器。</para>
/// </remarks>
public class OperationRecordArchiveService(
    ITenantDatabaseEnumerator databaseEnumerator,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    IDbContextProvider<MyProjectDbContext> dbContextProvider,
    IClock clock,
    ILogger<OperationRecordArchiveService> logger) : IOperationRecordArchiveService
{
    /// <inheritdoc />
    public async Task<OperationRecordArchiveResult> ArchiveOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var databases = await databaseEnumerator.GetDatabasesAsync(ConnectionStringNames.Default, cancellationToken);
        var archived = 0;
        var failed = 0;

        foreach (var database in databases)
        {
            try
            {
                archived += await ArchiveDatabaseAsync(database, cutoffUtc, batchSize, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Archiving operation records failed for database {Database}.", database);
            }
        }

        return new OperationRecordArchiveResult(archived, databases.Count, failed);
    }

    private async Task<int> ArchiveDatabaseAsync(
        TenantDatabase database,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;

        // 先切租户再开工作单元：工作单元按开启时的租户绑定连接，顺序反过来会被连接归属校验拒绝
        using (currentTenant.Change(database.TenantId))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 每批一个工作单元：事务有界，跟踪的实体随工作单元一起释放
                using var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
                var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

                // **IgnoreQueryFilters 是必需的，不是优化。**OperationRecord 实现 IMultiTenant，
                // 带着租户全局过滤器，只放行当前上下文那一个租户的行；同一个库里其余租户的记录
                // 不加这一句就永远留着——而且不报错。
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

                // 一次提交覆盖"写归档 + 删原表"：两者必须同生共死。
                // 分两次提交会出现"已删除但没归档"的窗口，而那是不可逆的数据丢失。
                await unitOfWork.CompleteAsync(cancellationToken);

                total += batch.Count;
                if (batch.Count < batchSize)
                {
                    break;
                }
            }
        }

        return total;
    }

    private static OperationRecordArchive ToArchive(OperationRecord record, DateTime archivedTime) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        ActorTenantId = record.ActorTenantId,
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
