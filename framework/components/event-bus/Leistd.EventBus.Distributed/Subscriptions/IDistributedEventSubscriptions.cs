using Leistd.EventBus.Events;

namespace Leistd.EventBus.Distributed.Subscriptions;

/// <summary>本服务已启用的对外订阅，传输据此建立订阅与路由。</summary>
/// <remarks>测试事件关闭时不在其中；事件类型实现 <see cref="IInternalDistributedEvent"/> 的订阅只在本服务内投递，也不在其中。</remarks>
public interface IDistributedEventSubscriptions
{
    /// <summary>全部有效的对外订阅，包括启用时的内置测试事件。</summary>
    IReadOnlyList<DistributedEventSubscription> GetAll();
}
