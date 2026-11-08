using Leistd.EventBus.Distributed.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.EventBus.Distributed.Loopback;

internal sealed class LoopbackEventTransport(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IDistributedEventTransport
{
    public string Name => "Loopback";

    public async Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(new RawDistributedEvent
            {
                Body = DistributedEventCodec.Encode(message),
                Headers = message.Headers,
                Coordinates = new Dictionary<string, string> { ["transport"] = Name },
                ReceivedAt = timeProvider.GetUtcNow().UtcDateTime
            }, cancellationToken);
            return new DistributedEventSendResult { Status = DistributedEventSendStatus.Succeeded };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new DistributedEventSendResult
            {
                Status = DistributedEventSendStatus.TransientFailure,
                ErrorCode = "loopback-delivery-failed",
                Error = exception.Message
            };
        }
    }

    public ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DistributedEventCodec.Decode(raw));
    }
}
