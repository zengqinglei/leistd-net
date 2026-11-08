using Leistd.EventBus.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Leistd.EventBus.Tests.TestDoubles;

internal static class EventTransportTestHost
{
    public static IHost Create(Action<IServiceCollection> configure) => new HostBuilder()
        .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMQ"] = "amqp://127.0.0.1:1",
            ["ConnectionStrings:Kafka"] = "127.0.0.1:1"
        }))
        .ConfigureServices(services =>
        {
            services.AddDistributedEventBus(o =>
            { o.Outbox.DispatchEnabled = false; o.Inbox.ReceiveEnabled = false; o.Inbox.ProcessingEnabled = false; });
            configure(services);
        }).Build();
}
