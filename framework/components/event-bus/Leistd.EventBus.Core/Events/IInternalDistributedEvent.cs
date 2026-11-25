namespace Leistd.EventBus.Events;

/// <summary>声明只由本服务消费的分布式事件：发件箱不经传输，在同库直接写入本服务收件箱。</summary>
/// <remarks>
/// 路由在发布时随发件箱记录冻结；传输不为它建立订阅，传输收到的同名消息也不会交给它的消费者。
/// 共库中所有开启发件箱派发或收件箱处理的实例须登记相同、完整的消费者；派发实例找不到任何消费者时，该记录进入死信。
/// </remarks>
public interface IInternalDistributedEvent : IDistributedEvent
{
}
