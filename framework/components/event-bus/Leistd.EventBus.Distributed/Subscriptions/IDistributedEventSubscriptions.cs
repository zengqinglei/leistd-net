namespace Leistd.EventBus.Distributed.Subscriptions;

/// <summary>本服务已启用的订阅；测试事件关闭时不在其中。</summary>
public interface IDistributedEventSubscriptions
{
    /// <summary>全部有效订阅，包括启用时的内置测试事件。</summary>
    IReadOnlyList<DistributedEventSubscription> GetAll();
}
