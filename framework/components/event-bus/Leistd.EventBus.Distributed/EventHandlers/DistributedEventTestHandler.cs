using Leistd.EventBus.EventHandlers;

using Leistd.EventBus.Distributed.Events;

namespace Leistd.EventBus.Distributed.EventHandlers;

internal sealed class DistributedEventTestHandler : IEventHandler<DistributedEventTestEvent>
{
    public Task HandleAsync(DistributedEventTestEvent @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
