using Leistd.Timing;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// <see cref="INotificationStore"/> 的 EF Core 实现。
/// </summary>
/// <typeparam name="TDbContext">宿主 DbContext 类型（需包含 NotificationRecord 配置）。</typeparam>
public class EfCoreNotificationStore<TDbContext>(TDbContext dbContext, IClock clock) : INotificationStore
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public async Task SaveAsync(NotificationOutputDto notification, string userId, CancellationToken ct = default)
    {
        var record = NotificationRecord.FromDto(notification, userId);
        dbContext.Set<NotificationRecord>().Add(record);
        // CreationTime / CreatorId 由审计拦截器在 SaveChanges 时填充
        await dbContext.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationOutputDto>> GetByUserAsync(string userId, int maxCount = 50, CancellationToken ct = default)
    {
        var records = await dbContext.Set<NotificationRecord>()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreationTime)
            .Take(maxCount)
            .ToListAsync(ct);

        return records.Select(r => r.ToDto()).ToList();
    }

    /// <inheritdoc/>
    public async Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(notificationId, out var id))
            return;

        var record = await dbContext.Set<NotificationRecord>()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (record is { IsRead: false })
        {
            record.IsRead = true;
            record.ReadAt = clock.Now;
            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc/>
    public async Task MarkAllAsReadAsync(string userId, CancellationToken ct = default)
    {
        var records = await dbContext.Set<NotificationRecord>()
            .Where(x => x.UserId == userId && !x.IsRead)
            .ToListAsync(ct);

        if (records.Count == 0)
            return;

        var now = clock.Now;
        foreach (var record in records)
        {
            record.IsRead = true;
            record.ReadAt = now;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
        => dbContext.Set<NotificationRecord>().CountAsync(x => x.UserId == userId && !x.IsRead, ct);
}
