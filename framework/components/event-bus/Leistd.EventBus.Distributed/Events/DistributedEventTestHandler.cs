using Leistd.EventBus.EventHandlers;

namespace Leistd.EventBus.Distributed.Events;

internal sealed class DistributedEventTestHandler : IEventHandler<DistributedEventTestEvent>
{
    public Task HandleAsync(DistributedEventTestEvent @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
