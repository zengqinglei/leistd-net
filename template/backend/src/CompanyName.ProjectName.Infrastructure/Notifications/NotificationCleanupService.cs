using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Notifications.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Leistd.Notifications.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;

namespace CompanyName.ProjectName.Infrastructure.Notifications;

/// <summary>
/// 基于 EF Core 的通知清理实现：对 <see cref="NotificationRecord"/> 表按用户维度删除。
/// 采用 <c>RemoveRange + SaveChangesAsync</c>（而非 <c>ExecuteDeleteAsync</c>），
/// 以兼容关系型与 InMemory 等所有 EF 提供程序，并复用 DbContext 上的审计/事件拦截器。
/// </summary>
/// <remarks>
/// 上下文经 <see cref="IDbContextProvider{TDbContext}"/> 按当前租户取，不直接注入：直接注入的实例在控制器激活时就按宿主库创建，
/// 分库租户下读写都会落到宿主库（框架在同一作用域里再按租户取上下文时会拒绝，表现为通知接口全部 500）。
/// </remarks>
public class NotificationCleanupService(IDbContextProvider<MyProjectDbContext> dbContextProvider) : INotificationCleanupService
{
    /// <inheritdoc />
    public async Task<int> ClearAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var records = await dbContext
            .Set<NotificationRecord>()
            .Where(n => n.UserId == userId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return 0;
        }

        dbContext.Set<NotificationRecord>().RemoveRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    /// <inheritdoc />
    public async Task<int> ClearOneAsync(
        string userId,
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(notificationId, out var id))
        {
            return 0;
        }

        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var record = await dbContext
            .Set<NotificationRecord>()
            .FirstOrDefaultAsync(n => n.UserId == userId && n.Id == id, cancellationToken);

        if (record is null)
        {
            return 0;
        }

        dbContext.Set<NotificationRecord>().Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }
}
