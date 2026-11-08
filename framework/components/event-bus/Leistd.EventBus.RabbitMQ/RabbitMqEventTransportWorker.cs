using Microsoft.Extensions.Hosting;

namespace Leistd.EventBus.RabbitMQ;

internal sealed class RabbitMqEventTransportWorker(RabbitMqEventTransport transport) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => transport.RunAsync(stoppingToken);
}
