using Leistd.EventBus.Events;

namespace CompanyName.ProjectName.Application.RealTime;

/// <summary>
/// 角色列表已变化。资源键在发布时按当时的作用域定案，提交后推送不再依赖上下文。
/// </summary>
/// <param name="resourceKey">变化所在作用域的角色列表资源键。</param>
public sealed class RoleListChangedEvent(string resourceKey) : LocalEvent
{
    /// <summary>变化所在作用域的角色列表资源键。</summary>
    public string ResourceKey { get; } = resourceKey;
}
