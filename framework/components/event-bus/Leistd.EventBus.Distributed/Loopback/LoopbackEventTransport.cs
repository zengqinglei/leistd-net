using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.EventBus.Distributed.Loopback;

internal sealed class LoopbackEventTransport(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    IOptionsMonitor<DistributedEventOptions> options, ILogger<LoopbackEventTransport>? logger = null) : IDistributedEventTransport
{
    public string Name => "Loopback";

    public async Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.CurrentValue.Inbox.ReceiveEnabled)
            return new DistributedEventSendResult { Status = DistributedEventSendStatus.Succeeded };
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
            logger?.LogWarning(exception, "Loopback distributed event {EventId} delivery failed.", message.EventId);
            return new DistributedEventSendResult
            {
                Status = DistributedEventSendStatus.TransientFailure,
                ErrorCode = "loopback-delivery-failed",
                Error = exception.GetType().FullName
            };
        }
    }

    public ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DistributedEventCodec.Decode(raw));
    }
}
