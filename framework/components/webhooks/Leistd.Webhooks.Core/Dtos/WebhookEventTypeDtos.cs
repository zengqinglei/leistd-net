namespace Leistd.Webhooks.Dtos;

/// <summary>一个可订阅的事件类型。</summary>
public sealed record WebhookEventTypeOutputDto
{
    /// <summary>类型名。</summary>
    public required string Name { get; init; }

    /// <summary>分组。</summary>
    public required string Group { get; init; }

    /// <summary>说明，通常是本地化键，由客户端解释；没有说明时为 <see langword="null"/>。</summary>
    public string? Description { get; init; }
}
