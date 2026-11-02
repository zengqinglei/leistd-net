namespace Leistd.Webhooks.Publishing;

/// <summary>在当前工作单元内发布 webhook 事件，业务发布事件的唯一入口。</summary>
/// <remarks>
/// <para>事件与投递记录随调用方的工作单元一起提交：业务回滚时事件也回滚，提交后不丢失。
/// 提交后立即尝试投递，失败按退避表持久重试。</para>
/// <para>只投给当前租户（宿主上下文下为宿主）里订阅了该类型的启用订阅；没有匹配订阅时只记事件，不产生投递。</para>
/// <para>在本地事件处理器里调用时，处理器须声明
/// <c>[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]</c>：默认的提交后阶段里环境工作单元已清空，调用会抛出。</para>
/// <para>投递语义是至少一次、不保证顺序；接收方按 <c>webhook-id</c> 去重。</para>
/// </remarks>
/// <example>
/// <code>
/// [UnitOfWork]
/// public async Task MarkPaidAsync(Guid orderId, CancellationToken ct)
/// {
///     // ...更新订单
///     await webhookPublisher.PublishAsync("order.paid", new { orderId }, ct);
/// }
/// </code>
/// </example>
public interface IWebhookPublisher
{
    /// <summary>发布一个事件：在当前工作单元里写入事件与每个匹配订阅的投递记录。</summary>
    /// <param name="eventType">事件类型名，须已由 <see cref="Definitions.IWebhookDefinitionProvider"/> 登记。</param>
    /// <param name="data">事件数据，发布时序列化为信封里的 <c>data</c> 并冻结，之后修改对象不影响已发布的载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ArgumentException"><paramref name="eventType"/> 为空白或未登记。</exception>
    /// <exception cref="InvalidOperationException">没有活动的工作单元；发布器不自开事务冒充业务事务。</exception>
    Task PublishAsync(string eventType, object? data, CancellationToken cancellationToken = default);
}
