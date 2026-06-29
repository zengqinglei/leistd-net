using System.Text.Json;
using Leistd.Auditing;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// 通知持久化实体。实现创建审计接口，审计字段由审计拦截器统一填充。
/// </summary>
public class NotificationRecord : ICreationAuditedObject
{
    /// <summary>通知 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>目标用户 ID。</summary>
    public string UserId { get; set; } = default!;

    /// <summary>通知标题。</summary>
    public string Title { get; set; } = default!;

    /// <summary>通知内容。</summary>
    public string? Content { get; set; }

    /// <summary>通知类型（字符串）。</summary>
    public string Type { get; set; } = NotificationTypes.System;

    /// <summary>点击跳转路由。</summary>
    public string? Link { get; set; }

    /// <summary>图标标识。</summary>
    public string? Icon { get; set; }

    /// <summary>是否已读。</summary>
    public bool IsRead { get; set; }

    /// <summary>已读时间。</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>关联实体 ID。</summary>
    public string? RelatedEntityId { get; set; }

    /// <summary>关联实体类型。</summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>扩展元数据（JSON）。</summary>
    public string? MetadataJson { get; set; }

    // --- 审计字段（ICreationAuditedObject，由审计拦截器填充）---

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }

    /// <summary>
    /// 从 <see cref="NotificationOutputDto"/> 创建持久化实体。
    /// </summary>
    public static NotificationRecord FromDto(NotificationOutputDto notification, string userId)
    {
        return new NotificationRecord
        {
            Id = Guid.TryParse(notification.Id, out var id) ? id : Guid.CreateVersion7(),
            UserId = userId,
            Title = notification.Title,
            Content = notification.Content,
            Type = notification.Type,
            Link = notification.Link,
            Icon = notification.Icon,
            IsRead = notification.IsRead,
            RelatedEntityId = notification.RelatedEntityId,
            RelatedEntityType = notification.RelatedEntityType,
            MetadataJson = notification.Metadata != null
                ? JsonSerializer.Serialize(notification.Metadata)
                : null
            // CreationTime / CreatorId 由审计拦截器填充
        };
    }

    /// <summary>
    /// 转换为 <see cref="NotificationOutputDto"/> DTO。
    /// </summary>
    public NotificationOutputDto ToDto()
    {
        return new NotificationOutputDto
        {
            Id = Id.ToString("N"),
            Title = Title,
            Content = Content,
            Type = Type,
            Link = Link,
            Icon = Icon,
            IsRead = IsRead,
            CreationTime = CreationTime,
            RelatedEntityId = RelatedEntityId,
            RelatedEntityType = RelatedEntityType,
            Metadata = MetadataJson != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(MetadataJson)
                : null
        };
    }
}
