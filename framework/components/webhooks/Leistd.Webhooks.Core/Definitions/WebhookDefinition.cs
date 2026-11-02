namespace Leistd.Webhooks.Definitions;

/// <summary>一个已登记的 webhook 事件类型。</summary>
public sealed class WebhookDefinition
{
    /// <summary>组件内置的测试事件类型，管理端点的"发送测试事件"使用；业务不得重复登记。</summary>
    public const string TestEventType = "webhook.test";

    /// <summary>内置测试事件所在的分组。</summary>
    public const string TestEventGroup = "webhooks";

    /// <summary>创建事件类型定义。</summary>
    /// <param name="name">类型名。</param>
    /// <param name="group">分组。</param>
    /// <param name="description">说明，通常是本地化键。</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> 或 <paramref name="group"/> 为空白。</exception>
    public WebhookDefinition(string name, string group, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        Name = name;
        Group = group;
        Description = description;
    }

    /// <summary>类型名（全局唯一标识），即信封里的 <c>type</c>。</summary>
    public string Name { get; }

    /// <summary>分组。</summary>
    public string Group { get; }

    /// <summary>说明，通常是本地化键；为 <see langword="null"/> 表示没有说明。</summary>
    public string? Description { get; }
}
