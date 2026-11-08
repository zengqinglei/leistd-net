using System.Globalization;
using System.Text;
using Confluent.Kafka.Admin;
using Confluent.Kafka;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Kafka.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.Kafka;

internal sealed class KafkaEventTransport(
    IOptions<KafkaEventTransportOptions> options,
    IOptionsMonitor<DistributedEventOptions> distributed,
    IConfiguration configuration,
    IHostEnvironment environment,
    IDistributedEventSubscriptions subscriptions,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<KafkaEventTransport> logger) : IDistributedEventTransport, IDisposable
{
    private readonly object _clientGate = new();
    private readonly SemaphoreSlim _topicGate = new(1, 1);
    private readonly HashSet<string> _topics = new(StringComparer.Ordinal);
    private IProducer<string, byte[]>? _producer;
    private IAdminClient? _admin;
    private int _consumerRunning;
    private int _disposed;
    public string Name => "Kafka";
    private string BootstrapServers => configuration.GetConnectionString(options.Value.BootstrapServersConnectionStringName)!;

    public async Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            var topic = options.Value.TopicPrefix + message.EventName;
            if (topic.Length > 249 || topic is "." or ".." || topic.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
                return Failure(ErrorCode.TopicException);
            await EnsureTopicAsync(topic, cancellationToken);
            var headers = new Headers();
            foreach (var header in message.Headers) headers.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
            var result = await Producer().ProduceAsync(topic, new Message<string, byte[]>
            {
                Key = message.OrderingKey ?? message.EventId.ToString(), Value = DistributedEventCodec.Encode(message), Headers = headers
            }, cancellationToken);
            return result.Status == PersistenceStatus.Persisted
                ? new() { Status = DistributedEventSendStatus.Succeeded }
                : new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "kafka-unconfirmed", Error = "Kafka did not confirm persistence." };
        }
        catch (ProduceException<string, byte[]> exception) when (!cancellationToken.IsCancellationRequested) { return Failure(exception.Error.Code); }
        catch (CreateTopicsException exception) when (!cancellationToken.IsCancellationRequested)
        { return Failure(exception.Results.First(x => x.Error.IsError).Error.Code); }
        catch (KafkaException exception) when (!cancellationToken.IsCancellationRequested) { return Failure(exception.Error.Code); }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Kafka could not confirm a distributed event publication.");
            return new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = "kafka-unavailable", Error = "Kafka is unavailable; publication can be retried." };
        }
    }

    private static DistributedEventSendResult Failure(ErrorCode code) => new()
    {
        Status = code is ErrorCode.MsgSizeTooLarge or ErrorCode.TopicException or ErrorCode.TopicAuthorizationFailed or ErrorCode.UnknownTopicOrPart
            ? DistributedEventSendStatus.PermanentFailure : DistributedEventSendStatus.TransientFailure,
        ErrorCode = code == ErrorCode.MsgSizeTooLarge ? "message-too-large" : "kafka-" + code.ToString().ToLowerInvariant(),
        Error = "Kafka could not persist the message (" + code + ")."
    };

    public ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DistributedEventCodec.Decode(raw));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        if (!distributed.CurrentValue.Inbox.ReceiveEnabled) return;
        var topics = subscriptions.GetAll().Select(x => options.Value.TopicPrefix + x.EventName).Distinct().ToArray();
        if (topics.Length == 0) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var topic in topics) await EnsureTopicAsync(topic, ct);
                using var consumer = new ConsumerBuilder<Ignore, byte[]>(new ConsumerConfig
                {
                    BootstrapServers = BootstrapServers, GroupId = options.Value.GroupId ?? environment.ApplicationName,
                    EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest,
                    AllowAutoCreateTopics = false
                }).Build();
                consumer.Subscribe(topics);
                Volatile.Write(ref _consumerRunning, 1);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var received = consumer.Consume(ct);
                        var raw = new RawDistributedEvent
                        {
                            Body = received.Message.Value?.ToArray() ?? [], ReceivedAt = clock.GetUtcNow().UtcDateTime,
                            Headers = ReadHeaders(received.Message.Headers),
                            Coordinates = new Dictionary<string, string>
                            {
                                ["topic"] = received.Topic,
                                ["partition"] = received.Partition.Value.ToString(CultureInfo.InvariantCulture),
                                ["offset"] = received.Offset.Value.ToString(CultureInfo.InvariantCulture)
                            }
                        };
                        // 持久失败时不再 Consume：同一分区的后续位点不会越过当前消息而被提交。
                        while (!ct.IsCancellationRequested)
                        {
                            try
                            {
                                await using var scope = scopes.CreateAsyncScope();
                                await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(raw, ct);
                                break;
                            }
                            catch (Exception exception) when (!ct.IsCancellationRequested)
                            {
                                logger.LogWarning(exception, "Kafka delivery persistence failed; the current offset remains uncommitted.");
                                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                            }
                        }
                        ct.ThrowIfCancellationRequested();
                        // Commit(result) 只提交此分区的 offset+1。失败时重建消费者，让 broker 从未提交位点重新投递。
                        consumer.Commit(received);
                    }
                }
                finally
                {
                    Volatile.Write(ref _consumerRunning, 0);
                    consumer.Close();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                Volatile.Write(ref _consumerRunning, 0);
                logger.LogWarning(exception, "Kafka distributed event consumption is unavailable; it will reconnect.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    internal bool CheckHealth()
    {
        try
        {
            if (distributed.CurrentValue.Inbox.ReceiveEnabled && subscriptions.GetAll().Count > 0 && Volatile.Read(ref _consumerRunning) == 0) return false;
            return Admin().GetMetadata(TimeSpan.FromSeconds(2)).Brokers.Count > 0;
        }
        catch (KafkaException) { return false; }
    }

    private IProducer<string, byte[]> Producer()
    {
        lock (_clientGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return _producer ??= new ProducerBuilder<string, byte[]>(new ProducerConfig
            {
                BootstrapServers = BootstrapServers, Acks = Acks.All, EnableIdempotence = true,
                AllowAutoCreateTopics = false
            }).Build();
        }
    }
    private IAdminClient Admin()
    {
        lock (_clientGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return _admin ??= new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        }
    }

    private async Task EnsureTopicAsync(string topic, CancellationToken ct)
    {
        if (!options.Value.AutoCreateTopics) return;
        await _topicGate.WaitAsync(ct);
        try
        {
            if (_topics.Contains(topic)) return;
            try
            {
                await Admin().CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }],
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) }).WaitAsync(ct);
            }
            catch (CreateTopicsException exception) when (exception.Results.All(x => x.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError)) { }
            _topics.Add(topic);
        }
        finally { _topicGate.Release(); }
    }

    private static Dictionary<string, string> ReadHeaders(Headers? headers)
    {
        var result = new Dictionary<string, string>();
        if (headers is not null)
            foreach (var header in headers) result[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes() ?? []);
        return result;
    }

    public void Dispose()
    {
        lock (_clientGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _producer?.Dispose(); _admin?.Dispose(); _topicGate.Dispose();
        }
    }
}
