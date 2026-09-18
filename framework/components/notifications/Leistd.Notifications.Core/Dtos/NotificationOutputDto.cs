namespace Leistd.Notifications.Dtos;

/// <summary>
/// 通知传输对象（DTO）—— 不含持久化细节。
/// </summary>
public record NotificationOutputDto
{
    /// <summary>
    /// 该用户这条通知记录的 ID；标记已读用的就是它。
    /// </summary>
    /// <remarks>
    /// 必填、无默认值：身份只在两处产生——发布器按收件人定案，或持久化读取时映射回来。
    /// 留一个"自己生成 Guid"的默认值等于开了第二个身份入口，而那正是同一份内容扇出给
    /// 多个用户时两条记录带同一主键的来源。
    /// </remarks>
    public required string Id { get; init; }

    /// <summary>通知标题。</summary>
    public string Title { get; init; } = default!;

    /// <summary>通知内容（可选）。</summary>
    public string? Content { get; init; }

    /// <summary>通知类型（业务自定义的字符串；未指定时为 <see cref="NotificationInputDto.DefaultType"/>）。</summary>
    public string Type { get; init; } = NotificationInputDto.DefaultType;

    /// <summary>点击跳转路由（可选）。</summary>
    public string? Link { get; init; }

    /// <summary>图标标识（可选，由前端解释）。</summary>
    public string? Icon { get; init; }

    /// <summary>是否已读。</summary>
    public bool IsRead { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    /// <remarks>必填：与 <see cref="Id"/> 同一处定案，持久化层不生成也不替换。</remarks>
    public required DateTime CreationTime { get; init; }

    /// <summary>关联实体 ID（可选）。</summary>
    public string? RelatedEntityId { get; init; }

    /// <summary>关联实体类型（可选）。</summary>
    public string? RelatedEntityType { get; init; }

    /// <summary>扩展元数据（可选）。</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
