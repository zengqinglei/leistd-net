using Leistd.EventBus.Events;

namespace Leistd.EventBus.Abstractions;

/// <summary>
/// 完成边界对本地事件发布的介入点：有活动边界时把事件推迟到该边界完成时发布。
/// </summary>
/// <remarks>
/// <para>本接口<b>只声明契约</b>，实现由拥有完成边界的组件提供。边界是什么、什么时候完成、
/// 是否带事务，都由实现决定——本包不认识那些概念，依赖方向因此是单向的。</para>
/// <para>没有注册实现时总线按原样立即分发。</para>
/// </remarks>
public interface ILocalEventDeferrer
{
    /// <summary>
    /// 尝试把事件交给当前活动的完成边界。
    /// </summary>
    /// <param name="event">待发布的事件。</param>
    /// <returns>
    /// <see langword="true"/> 表示已接管，调用方<b>不再分发</b>；
    /// <see langword="false"/> 表示当前没有活动边界或该事件不适用，调用方照常分发。
    /// </returns>
    bool TryDefer(IEvent @event);
}
