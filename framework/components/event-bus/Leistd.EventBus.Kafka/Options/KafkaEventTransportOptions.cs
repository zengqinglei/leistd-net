namespace Leistd.EventBus.Kafka.Options;

/// <summary>传输部署配置。</summary>
public sealed class KafkaEventTransportOptions
{
    /// <summary>默认配置节。</summary>
    public const string SectionName = "Leistd:EventBus:Kafka";

    /// <summary>bootstrap servers 所在连接串名称，默认 Kafka。</summary>
    public string BootstrapServersConnectionStringName { get; set; } = "Kafka";

    /// <summary>主题前缀，默认为空；主题为前缀加事件名。</summary>
    public string TopicPrefix { get; set; } = string.Empty;

    /// <summary>消费组；null 使用宿主应用名。</summary>
    public string? GroupId { get; set; } = null;

    /// <summary>是否允许创建缺失主题，默认开启；关闭时由宿主提前建主题。</summary>
    public bool AutoCreateTopics { get; set; } = true;
}
