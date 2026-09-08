namespace CompanyName.ProjectName.Infrastructure.Notifications;

/// <summary>
/// 通知清理服务：框架 <c>INotificationStore</c> 仅提供读取 / 标记已读能力，
/// 未暴露删除接口。此处直接基于 EF Core 对当前用户的通知记录做持久化清空，
/// 供“清空全部（Clear All）”场景使用。
/// </summary>
public interface INotificationCleanupService
{
    /// <summary>清空指定用户的全部通知（持久删除）。</summary>
    /// <param name="userId">目标用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响的记录数。</returns>
    Task<int> ClearAllAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>删除指定用户的单条通知（持久删除）。</summary>
    /// <param name="userId">目标用户标识。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响的记录数（0 或 1）。</returns>
    Task<int> ClearOneAsync(string userId, string notificationId, CancellationToken cancellationToken = default);
}
