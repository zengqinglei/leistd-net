namespace Leistd.EventBus.RabbitMQ.Options;

/// <summary>传输部署配置。</summary>
public sealed class RabbitMqEventTransportOptions
{
    /// <summary>默认配置节。</summary>
    public const string SectionName = "Leistd:EventBus:RabbitMQ";

    /// <summary>amqp URI 所在连接串名称，默认 RabbitMQ。</summary>
    public string ConnectionStringName { get; set; } = "RabbitMQ";

    /// <summary>持久 topic 交换机，默认 leistd.events。</summary>
    public string Exchange { get; set; } = "leistd.events";

    /// <summary>持久单活队列名；null 使用宿主应用名。</summary>
    public string? QueueName { get; set; } = null;

    /// <summary>接收预取上限，默认 32；入库仍串行。</summary>
    public ushort PrefetchCount { get; set; } = 32;
}
