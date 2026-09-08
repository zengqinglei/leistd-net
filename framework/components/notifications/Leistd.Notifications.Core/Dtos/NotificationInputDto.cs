using Leistd.Notifications.Constants;

namespace Leistd.Notifications.Dtos;

/// <summary>
/// 要发布的通知内容——尚未归属到任何收件人。
/// </summary>
/// <remarks>
/// 与 <see cref="NotificationOutputDto"/> 的区别就是身份：本类型只描述"通知说了什么"，
/// 而<b>谁的通知、哪一条</b>由发布器在收件人边界定案（<c>Id</c>、<c>CreationTime</c>、<c>IsRead</c>）。
/// 两者曾是同一个类型，于是同一份内容发给第二个用户时两条记录带着同一个主键，
/// 第二次落库直接冲突；而"客户端看到的 ID 是公告 ID 还是这个人的记录 ID"也说不清。
/// 现在说得清：<see cref="NotificationOutputDto.Id"/> 恒为<b>该用户的那条记录</b>，
/// 标记已读用的就是它。
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
