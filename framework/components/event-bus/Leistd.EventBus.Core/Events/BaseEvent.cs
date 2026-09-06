namespace Leistd.EventBus.Events;

/// <summary>
/// 提供事件标识和发生时间基类。
/// </summary>
/// <remarks>
/// 时间取自 <see cref="TimeProvider"/> 而不是 <c>DateTime.UtcNow</c>，使冻结时间的测试对事件时间戳同样生效。
/// 事件由领域代码 <c>new</c> 出来、拿不到容器，因此默认落 <see cref="TimeProvider.System"/>；
/// 需要在测试中控制事件时间时，用带 <c>occurredOn</c> 的受保护构造函数显式传入。
/// </remarks>
public abstract class BaseEvent : IEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTime OccurredOn { get; }

    /// <summary>
    /// 使用系统时间源创建事件。
    /// </summary>
    protected BaseEvent() : this(TimeProvider.System.GetUtcNow().UtcDateTime)
    {
    }

    /// <summary>
    /// 使用指定发生时间创建事件。
    /// </summary>
    /// <param name="occurredOn">事件发生时刻（应为 UTC）</param>
    protected BaseEvent(DateTime occurredOn)
    {
        // 有序 Guid：事件按 Id 排序即接近按发生顺序排序，与实体主键口径一致
        EventId = Guid.CreateVersion7();
        OccurredOn = occurredOn;
    }
}
