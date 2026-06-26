namespace Leistd.RealTime;

/// <summary>
/// 业务事件推送器 —— 面向"资源订阅"的细粒度实时推送。
/// </summary>
/// <remarks>
/// 与通知（Notifications）解耦：本接口只负责把任意事件推给订阅了某资源的客户端，
/// 不感知通知领域模型。适用于产品档案变更、工作流状态变更等业务事件。
/// </remarks>
public interface IBusinessEventPublisher
{
    /// <summary>
    /// 推送事件给订阅了指定资源的客户端。
    /// </summary>
    /// <typeparam name="TEvent">事件数据类型</typeparam>
    /// <param name="resourceKey">资源标识（如 "product-profile:{ownerId}"）</param>
    /// <param name="eventName">事件名（客户端据此监听，如 "ProductProfileUpdated"）</param>
    /// <param name="event">事件数据</param>
    /// <param name="ct">取消令牌</param>
    Task PublishToResourceAsync<TEvent>(string resourceKey, string eventName, TEvent @event, CancellationToken ct = default)
        where TEvent : class;
}
