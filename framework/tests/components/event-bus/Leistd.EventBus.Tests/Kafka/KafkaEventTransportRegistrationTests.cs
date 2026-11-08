using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Kafka.Options;
using Leistd.EventBus.Kafka;
using Leistd.EventBus.RabbitMQ;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.EventBus.Tests.Kafka;

/// <summary>Kafka 配置与传输选择；不连接 broker。</summary>
public sealed class KafkaEventTransportRegistrationTests
{
    /// <summary>工厂别名与传输契约指向同一实例；重复调用不重复登记健康检查。</summary>
    [Fact]
    public void Repeated_registration_keeps_one_transport_and_one_health_check()
    {
        using var host = EventTransportTestHost.Create(services =>
        { services.AddKafkaEventTransport(); services.AddKafkaEventTransport(); });
        Assert.Same(host.Services.GetRequiredService<IDistributedEventTransport>(), host.Services.GetRequiredService<KafkaEventTransport>());
        Assert.Single(host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations, x => x.Name == "event-bus-kafka");
    }

    /// <summary>两个不同 broker 不能按后登记顺序静默覆盖。</summary>
    [Fact]
    public void Different_transports_are_rejected_during_registration()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqEventTransport();
        Assert.Throws<InvalidOperationException>(() => services.AddKafkaEventTransport());
    }

    /// <summary>组名、主题前缀与连接名在选项边界报出。</summary>
    [Theory]
    [InlineData("GroupId")]
    [InlineData("TopicPrefix")]
    [InlineData("BootstrapServersConnectionStringName")]
    public void Invalid_options_report_the_configuration_key(string key)
    {
        using var host = EventTransportTestHost.Create(services => services.AddKafkaEventTransport(o =>
        {
            if (key == "GroupId") o.GroupId = "";
            if (key == "TopicPrefix") o.TopicPrefix = "invalid/";
            if (key == "BootstrapServersConnectionStringName") o.BootstrapServersConnectionStringName = "";
        }));
        var error = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<KafkaEventTransportOptions>>().Value);
        Assert.Contains("Leistd:EventBus:Kafka:" + key, error.Message);
    }
}
