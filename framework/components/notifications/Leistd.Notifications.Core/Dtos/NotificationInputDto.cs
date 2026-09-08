using Leistd.Notifications.Constants;

namespace Leistd.Notifications.Dtos;

/// <summary>
/// 要发布的通知内容——尚未归属到任何收件人。
/// </summary>
/// <remarks>
/// 同一内容可发布给多个收件人；发布器为每次发布创建独立的
/// <see cref="NotificationOutputDto"/>，确定记录标识、创建时间和未读状态。
/// </remarks>
public record NotificationInputDto
{
    /// <summary>通知标题。</summary>
    public required string Title { get; init; }

    /// <summary>通知内容（可选）。</summary>
    public string? Content { get; init; }

    /// <summary>通知类型（字符串，见 <see cref="NotificationTypes"/>）。</summary>
    public string Type { get; init; } = NotificationTypes.System;

    /// <summary>点击跳转路由（可选）。</summary>
    public string? Link { get; init; }

    /// <summary>图标标识（可选，由前端解释）。</summary>
    public string? Icon { get; init; }

    /// <summary>关联实体 ID（可选）。</summary>
    public string? RelatedEntityId { get; init; }

    /// <summary>关联实体类型（可选）。</summary>
    public string? RelatedEntityType { get; init; }

    /// <summary>扩展元数据（可选）。</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
