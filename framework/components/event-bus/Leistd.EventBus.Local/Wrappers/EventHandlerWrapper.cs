using Microsoft.Extensions.DependencyInjection;
using Leistd.EventBus.Events;
using Leistd.EventBus.EventHandlers;

namespace Leistd.EventBus.Local.Wrappers;

internal abstract class EventHandlerWrapper
{
    public abstract Task HandleAsync(
        IEvent @event,
        IServiceScopeFactory serviceScopeFactory,
        CancellationToken cancellationToken);
}

internal class EventHandlerWrapperImpl<TEvent> : EventHandlerWrapper
    where TEvent : IEvent
{
    /// <summary>
    /// 依次调用全部处理器，并在全部完成后抛出收集的异常。
    /// </summary>
    /// <remarks>
    /// <b>一个处理器失败不阻断其余处理器</b>。全部跑完后：单个异常原样上抛（保留类型），
    /// 多个包成 <see cref="AggregateException"/>——发布方因此仍会失败。
    /// 取消异常不参与聚合：它表示调用方主动放弃，不是处理器缺陷。
    /// </remarks>
    public override async Task HandleAsync(
        IEvent @event,
        IServiceScopeFactory serviceScopeFactory,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TEvent>>();

        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync((TEvent)@event, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            // 单个失败保留原异常类型，支持调用方按类型捕获。
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(
            $"{failures.Count} handler(s) failed while handling event '{typeof(TEvent).FullName}'.",
            failures);
    }
}
