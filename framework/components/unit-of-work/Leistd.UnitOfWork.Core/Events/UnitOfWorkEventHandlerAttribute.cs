namespace Leistd.UnitOfWork.Events;

/// <summary>
/// 指定事件处理器的工作单元执行阶段。
/// </summary>
/// <example>
/// <code><![CDATA[
/// [UnitOfWorkEventHandler(Phase = UnitOfWorkPhase.AfterCommit)]
/// public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
/// {
///     public async Task HandleAsync(UserCreatedEvent @event)
///     {
///         await _emailService.SendWelcomeEmail(@event.User.Email);
///     }
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class UnitOfWorkEventHandlerAttribute(UnitOfWorkPhase phase = UnitOfWorkPhase.AfterCommit) : Attribute
{
    /// <summary>
    /// 工作单元阶段（默认：AfterCommit）
    /// </summary>
    public UnitOfWorkPhase Phase { get; set; } = phase;
}
