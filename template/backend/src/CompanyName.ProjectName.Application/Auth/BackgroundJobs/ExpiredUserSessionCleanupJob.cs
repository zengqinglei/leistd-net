#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Tenancy;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.BackgroundJobs;

/// <summary>
/// 每天删除已过期的会话（最近活动早于空闲超时）。
/// </summary>
/// <remarks>
/// <para>登录时会顺手删掉此人自己的过期会话，但不再登录的用户没有这个时机：他们的会话行连同原始 IP
/// 会无限期留在表里。过期会话既不能再用于登录，设备列表也不显示，保留它没有任何读取方，所以不设保留期。</para>
/// <para>会话落在各自租户的库里：按物理库逐个执行，停用租户的库也清（数据不因停用就不占空间）；
/// 同一个库里有多个租户，所以删除时关掉租户过滤。有库失败或租户解析不出库时本轮报失败，
/// 调度器不记水位，下一个时段按截止时间一并清掉。</para>
/// </remarks>
internal sealed class ExpiredUserSessionCleanupJob(
    ITenantDatabaseRunner databaseRunner,
    IUnitOfWorkManager unitOfWorkManager,
    IRepository<UserSession, Guid> sessionRepository,
    IDataFilter dataFilter,
    IOptions<UserSessionOptions> options,
    IClock clock,
    ILogger<ExpiredUserSessionCleanupJob> logger) : IRecurringJob
{
    internal const string Name = "auth.sessions.cleanup";

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        var now = clock.Now;

        var result = await databaseRunner.ForEachDatabaseAsync(ConnectionStringNames.Default, activeOnly: false, async (_, ct) =>
        {
            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
            using (dataFilter.Disable<IMultiTenant>())
            {
                await sessionRepository.DeleteManyAsync(UserSession.ExpiredAt(now, options.Value.IdleTimeout), ct);
            }

            await unitOfWork.CompleteAsync(ct);
        }, cancellationToken);

        if (result.FailedDatabases.Count > 0 || result.UnresolvedTenants.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expired session cleanup failed for {result.FailedDatabases.Count} of {result.Databases} database(s); " +
                $"{result.UnresolvedTenants.Count} tenant(s) could not be resolved to a database.");
        }

        logger.LogInformation("Deleted expired sessions across {Databases} database(s).", result.Databases);
    }
}
#endif
