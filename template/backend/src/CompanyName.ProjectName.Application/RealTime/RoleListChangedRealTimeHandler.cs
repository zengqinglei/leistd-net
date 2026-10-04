using Leistd.EventBus.EventHandlers;
using Leistd.RealTime.Publishing;

namespace CompanyName.ProjectName.Application.RealTime;

/// <summary>
/// 角色列表变化提交之后，推送给订阅了同一作用域角色列表的客户端。
/// </summary>
/// <remarks>
/// 事件处理器默认在工作单元提交之后执行：回滚的写入不推送，客户端刷新时看到的就是已提交的数据。
/// 推送只是提示"该刷新了"，不携带数据——列表内容仍经受权限保护的查询接口获取。
/// </remarks>
public sealed class RoleListChangedRealTimeHandler(IBusinessEventPublisher publisher)
    : IEventHandler<RoleListChangedEvent>
{
    public Task HandleAsync(RoleListChangedEvent @event, CancellationToken cancellationToken = default)
        => publisher.PublishToResourceAsync(@event.ResourceKey, AppRealTimeResources.RolesChanged, new { }, cancellationToken);
}
