namespace Leistd.Notifications;

/// <summary>
/// 通知传输对象（DTO）—— 不含持久化细节。
/// </summary>
public record NotificationOutputDto
{
    /// <summary>通知 ID（默认有序 Guid v7）。</summary>
    public string Id { get; init; } = Guid.CreateVersion7().ToString("N");

    /// <summary>通知标题。</summary>
    public string Title { get; init; } = default!;

    /// <summary>通知内容（可选）。</summary>
    public string? Content { get; init; }

    /// <summary>通知类型（字符串，见 <see cref="NotificationTypes"/>）。</summary>
    public string Type { get; init; } = NotificationTypes.System;

    /// <summary>点击跳转路由（可选）。</summary>
    public string? Link { get; init; }

    /// <summary>图标标识（可选，由前端解释）。</summary>
    public string? Icon { get; init; }

    /// <summary>是否已读。</summary>
    public bool IsRead { get; init; }

    /// <summary>创建时间（由发布方或持久化层赋值）。</summary>
    public DateTime CreationTime { get; init; }

    /// <summary>关联实体 ID（可选）。</summary>
    public string? RelatedEntityId { get; init; }

    /// <summary>关联实体类型（可选）。</summary>
    public string? RelatedEntityType { get; init; }

    /// <summary>扩展元数据（可选）。</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
