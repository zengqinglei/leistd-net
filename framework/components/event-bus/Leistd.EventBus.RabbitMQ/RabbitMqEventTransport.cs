using System.Globalization;
using System.Text;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client;

namespace Leistd.EventBus.RabbitMQ;

internal sealed class RabbitMqEventTransport(
    IOptions<RabbitMqEventTransportOptions> options,
    IOptionsMonitor<DistributedEventOptions> distributed,
    IConfiguration configuration,
    IHostEnvironment environment,
    IDistributedEventSubscriptions subscriptions,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RabbitMqEventTransport> logger) : IDistributedEventTransport, IDisposable
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publisher;
    private IChannel? _consumer;
    private int _disposed;
    private int _consumerRunning;

    public string Name => "RabbitMQ";
    internal bool IsHealthy => _connection is { IsOpen: true } && _publisher is { IsOpen: true }
        && (!distributed.CurrentValue.Inbox.ReceiveEnabled || subscriptions.GetAll().Count == 0 || (_consumer is { IsOpen: true } && Volatile.Read(ref _consumerRunning) == 1));

    public async Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await EnsurePublisherAsync(cancellationToken);
            var properties = new BasicProperties
            {
                Persistent = true, ContentType = "application/json", MessageId = message.EventId.ToString(), Type = message.EventName,
                Headers = message.Headers.ToDictionary(x => x.Key, x => (object?)Encoding.UTF8.GetBytes(x.Value))
            };
            // SDK 的确认追踪开启，返回仅表示 broker 已确认，不以写入 socket 代替确认。
            await _publisher!.BasicPublishAsync(options.Value.Exchange, message.EventName, mandatory: false,
                basicProperties: properties, body: DistributedEventCodec.Encode(message), cancellationToken: cancellationToken);
            return new() { Status = DistributedEventSendStatus.Succeeded };
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == 311 && !cancellationToken.IsCancellationRequested)
        {
            return new() { Status = DistributedEventSendStatus.PermanentFailure, ErrorCode = "message-too-large", Error = "The broker rejected the message size." };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "RabbitMQ could not confirm a distributed event publication.");
            return new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "rabbitmq-unavailable", Error = "RabbitMQ did not confirm the publication; it can be retried." };
        }
        finally { _sendGate.Release(); }
    }

    public ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DistributedEventCodec.Decode(raw));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 发送侧也建立长连接；ReceiveEnabled=false 时不声明接收队列、不启动消费。
                await _sendGate.WaitAsync(ct);
                try { await EnsurePublisherAsync(ct); }
                finally { _sendGate.Release(); }
                if (!distributed.CurrentValue.Inbox.ReceiveEnabled || subscriptions.GetAll().Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }
                var connection = await GetConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                _consumer = channel;
                Volatile.Write(ref _consumerRunning, 0);
                var queue = options.Value.QueueName ?? environment.ApplicationName;
                await channel.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?> { ["x-single-active-consumer"] = true }, cancellationToken: ct);
                foreach (var name in subscriptions.GetAll().Select(x => x.EventName).Distinct())
                    await channel.QueueBindAsync(queue, options.Value.Exchange, name, cancellationToken: ct);
                await channel.BasicQosAsync(0, options.Value.PrefetchCount, global: false, cancellationToken: ct);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    // SDK 缓冲区仅在回调期间有效；持久失败时保留这份原消息，不能越过它处理后续。
                    var raw = new RawDistributedEvent
                    {
                        Body = delivery.Body.ToArray(), ReceivedAt = clock.GetUtcNow().UtcDateTime,
                        Headers = ReadHeaders(delivery.BasicProperties.Headers),
                        Coordinates = new Dictionary<string, string>
                        {
                            ["exchange"] = delivery.Exchange, ["routingKey"] = delivery.RoutingKey,
                            ["queue"] = queue, ["deliveryTag"] = delivery.DeliveryTag.ToString(CultureInfo.InvariantCulture),
                            ["messageId"] = delivery.BasicProperties.MessageId ?? string.Empty
                        }
                    };
                    while (!ct.IsCancellationRequested && channel.IsOpen)
                    {
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(raw, ct);
                            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
                            return;
                        }
                        catch (Exception exception) when (!ct.IsCancellationRequested)
                        {
                            logger.LogWarning(exception, "RabbitMQ delivery persistence failed; the current message remains unacknowledged.");
                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }
                    }
                };
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: ct);
                Volatile.Write(ref _consumerRunning, 1);
                while (channel.IsOpen && !ct.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(1), ct);
                _consumer = null;
                Volatile.Write(ref _consumerRunning, 0);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { Volatile.Write(ref _consumerRunning, 0); break; }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                _consumer = null;
                Volatile.Write(ref _consumerRunning, 0);
                logger.LogWarning(exception, "RabbitMQ distributed event consumption is unavailable; it will reconnect.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    private async Task EnsurePublisherAsync(CancellationToken ct)
    {
        if (_publisher is { IsOpen: true }) return;
        if (_publisher is not null) await _publisher.DisposeAsync();
        var connection = await GetConnectionAsync(ct);
        _publisher = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await _publisher.ExchangeDeclareAsync(options.Value.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        await _connectionGate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            if (_connection is not null) await _connection.DisposeAsync();
            var factory = new ConnectionFactory
            {
                Uri = new Uri(configuration.GetConnectionString(options.Value.ConnectionStringName)!),
                AutomaticRecoveryEnabled = false, ConsumerDispatchConcurrency = 1
            };
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally { _connectionGate.Release(); }
    }

    private static Dictionary<string, string> ReadHeaders(IDictionary<string, object?>? headers)
        => headers?.ToDictionary(x => x.Key, x => x.Value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
            null => string.Empty,
            _ => Convert.ToString(x.Value, CultureInfo.InvariantCulture) ?? string.Empty
        }) ?? new();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _consumer?.Dispose(); _publisher?.Dispose(); _connection?.Dispose();
        _connectionGate.Dispose(); _sendGate.Dispose();
    }
}
