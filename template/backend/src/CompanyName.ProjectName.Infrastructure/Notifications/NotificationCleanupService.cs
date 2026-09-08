using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Notifications.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Leistd.Notifications.EntityFrameworkCore.Entities;

namespace CompanyName.ProjectName.Infrastructure.Notifications;

/// <summary>
/// 基于 EF Core 的通知清理实现：对 <see cref="NotificationRecord"/> 表按用户维度删除。
/// 采用 <c>RemoveRange + SaveChangesAsync</c>（而非 <c>ExecuteDeleteAsync</c>），
/// 以兼容关系型与 InMemory 等所有 EF 提供程序，并复用 DbContext 上的审计/事件拦截器。
/// </summary>
public class NotificationCleanupService(MyProjectDbContext dbContext) : INotificationCleanupService
{
    /// <inheritdoc />
    public async Task<int> ClearAllAsync(string userId, CancellationToken cancellationToken = default)
    {
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
