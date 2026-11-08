using Microsoft.Extensions.Hosting;

namespace Leistd.EventBus.Kafka;

internal sealed class KafkaEventTransportWorker(KafkaEventTransport transport) : BackgroundService
{
    // SDK Consume 为阻塞 API，由后台线程运行，避免宿主启动被消费循环占用。
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => transport.RunAsync(stoppingToken), stoppingToken);
}
