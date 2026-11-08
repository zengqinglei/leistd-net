using System.Collections.Concurrent;
using Confluent.Kafka.Admin;
using Confluent.Kafka;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Leistd.EventBus.Kafka;
using Leistd.EventBus.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Leistd.EventBus.Tests.TestDoubles;

internal sealed class BrokerContractFixture(string kind, string connection, bool receiveEnabled) : IAsyncDisposable
{
    internal const string EventName = "leistd.contract";
    private readonly string _name = "leistd-contract-" + Guid.NewGuid().ToString("N");
    private IHost? _host;
    private bool _prepared;
    public BrokerReceiverState State { get; private set; } = new();
    public IDistributedEventTransport Transport => _host!.Services.GetRequiredService<IDistributedEventTransport>();
    private string Topic => _name + "-" + EventName;

    public static async Task<BrokerContractFixture> CreateAsync(string kind, string connection, bool receiveEnabled = true)
    {
        var fixture = new BrokerContractFixture(kind, connection, receiveEnabled);
        try
        {
            await fixture.PrepareAsync();
            await fixture.StartAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task StartAsync()
    {
        _host = new HostBuilder().ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:RabbitMQ"] = connection, ["ConnectionStrings:Kafka"] = connection }))
            .ConfigureServices(services =>
            {
                services.AddDistributedEventBus(o => { o.TestEvent.Enabled = false; o.Inbox.ReceiveEnabled = receiveEnabled; });
                services.AddDistributedEventHandler<BrokerContractHandler>("contract");
                services.AddSingleton(State);
                services.AddSingleton<IDistributedEventReceiver, BrokerContractReceiver>();
                if (kind == "rabbitmq")
                {
                    services.AddRabbitMqEventTransport(o => { o.Exchange = _name; o.QueueName = _name; });
                    services.AddHealthChecks().AddRabbitMqEventTransport();
                }
                else
                {
                    services.AddKafkaEventTransport(o => { o.GroupId = _name; o.TopicPrefix = _name + "-"; });
                    services.AddHealthChecks().AddKafkaEventTransport();
                }
            }).Build();
        await _host.StartAsync();
        await WaitAsync(async () => await HealthAsync() == HealthStatus.Healthy);
    }

    public Task StopAsync() => _host!.StopAsync();
    public async Task RestartAsync()
    {
        await StopAsync(); _host!.Dispose(); _host = null;
        State = new();
        await StartAsync();
    }
    public async Task<HealthStatus> HealthAsync()
        => (await _host!.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Tags.Contains("event-bus"))).Status;

    private async Task PrepareAsync()
    {
        if (kind == "rabbitmq")
        {
            await using var client = await new ConnectionFactory { Uri = new Uri(connection) }.CreateConnectionAsync();
            await using var channel = await client.CreateChannelAsync();
            await channel.ExchangeDeclareAsync(_name, ExchangeType.Topic, durable: true, autoDelete: false);
            await channel.QueueDeclareAsync(_name, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-single-active-consumer"] = true });
            await channel.QueueBindAsync(_name, _name, EventName);
        }
        else
        {
            using var client = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = connection }).Build();
            await client.CreateTopicsAsync([new TopicSpecification { Name = Topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }
        _prepared = true;
    }

    public async Task PublishRawAsync(byte[] body)
    {
        if (kind == "rabbitmq")
        {
            await using var client = await new ConnectionFactory { Uri = new Uri(connection) }.CreateConnectionAsync();
            await using var channel = await client.CreateChannelAsync(new CreateChannelOptions(true, true));
            await channel.BasicPublishAsync(_name, EventName, true, new BasicProperties { Persistent = true }, body);
        }
        else
        {
            using var client = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = connection, Acks = Acks.All }).Build();
            await client.ProduceAsync(Topic, new() { Key = "aggregate-1", Value = body });
        }
    }

    public static OutgoingDistributedEvent Message() => new()
    {
        EventId = Guid.NewGuid(), EventName = EventName, OccurredAt = DateTime.UtcNow, OrderingKey = "aggregate-1", Payload = "{}",
        Headers = new Dictionary<string, string> { ["leistd-correlation-id"] = "contract-request" }
    };

    public static Task WaitAsync(Func<bool> ready) => WaitAsync(() => Task.FromResult(ready()));
    public static async Task WaitAsync(Func<Task<bool>> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!await ready()) await Task.Delay(100, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.StopAsync(); _host.Dispose(); _host = null; }
        if (!_prepared) return;
        if (kind == "rabbitmq")
        {
            await using var client = await new ConnectionFactory { Uri = new Uri(connection) }.CreateConnectionAsync();
            await using var channel = await client.CreateChannelAsync();
            await channel.QueueDeleteAsync(_name, ifUnused: false, ifEmpty: false);
            await channel.ExchangeDeleteAsync(_name, ifUnused: false);
        }
        else
        {
            using var client = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = connection }).Build();
            await client.DeleteTopicsAsync([Topic]);
            try { await client.DeleteGroupsAsync([_name]); }
            catch (DeleteGroupsException exception) when (exception.Results.All(x => x.Error.Code is ErrorCode.GroupIdNotFound or ErrorCode.NoError)) { }
        }
    }
}

internal sealed class BrokerReceiverState
{
    public ConcurrentQueue<IncomingDistributedEvent> Persisted { get; } = new();
    public ConcurrentQueue<byte[]> Rejections { get; } = new();
    public ConcurrentDictionary<Guid, byte> Failures { get; } = new();
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public volatile bool FailRejections;
    public int RejectionAttempts;
}

internal sealed class BrokerContractReceiver(IDistributedEventTransport transport, BrokerReceiverState state) : IDistributedEventReceiver
{
    public async Task ReceiveAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        IncomingDistributedEvent message;
        try { message = await transport.DecodeAsync(raw, cancellationToken); }
        catch (DistributedEventDecodeException exception)
        { await RejectAsync(raw, exception.ReasonCode, exception.Message, cancellationToken); return; }
        state.Attempts.AddOrUpdate(message.EventId, 1, (_, value) => value + 1);
        if (state.Failures.ContainsKey(message.EventId)) throw new IOException("The persistence boundary is temporarily unavailable.");
        state.Persisted.Enqueue(message);
    }
    public Task RejectAsync(RawDistributedEvent raw, string reasonCode, string error, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref state.RejectionAttempts);
        if (state.FailRejections) throw new IOException("The rejection store is temporarily unavailable.");
        state.Rejections.Enqueue(raw.Body.ToArray());
        return Task.CompletedTask;
    }
}

[EventName(BrokerContractFixture.EventName)]
internal sealed class BrokerContractEvent : DistributedEvent { }
internal sealed class BrokerContractHandler : IEventHandler<BrokerContractEvent>
{
    public Task HandleAsync(BrokerContractEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
