namespace Leistd.EventBus.Abstractions;

/// <summary>在当前工作单元内持久发布分布式事件，随业务事务提交或回滚。</summary>
/// <remarks>只接受 IDistributedEvent；没有活动工作单元时抛 InvalidOperationException。同一工作单元内相同 EventId、事件名与冻结载荷只发布一次，内容冲突立即抛 InvalidOperationException；跨工作单元重复由数据库唯一约束拒绝。提交后立即发送，失败由持久扫描重试。</remarks>
public interface IDistributedEventBus : IEventBus
{
}
