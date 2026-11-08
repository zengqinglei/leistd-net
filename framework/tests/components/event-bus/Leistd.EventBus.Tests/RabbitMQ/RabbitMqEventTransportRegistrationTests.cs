using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.RabbitMQ.Options;
using Leistd.EventBus.RabbitMQ;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.EventBus.Tests.RabbitMQ;

/// <summary>传输权威登记、重复调用与配置键校验，不连接 broker。</summary>
public sealed class RabbitMqEventTransportRegistrationTests
{
    /// <summary>重复登记复用同一传输，只登记一次健康检查；未连接时不谎报健康。</summary>
    [Fact]
    public async Task Repeated_registration_shares_the_singleton_and_registers_one_health_check()
    {
        using var host = EventTransportTestHost.Create(services =>
        { services.AddRabbitMqEventTransport(); services.AddRabbitMqEventTransport(); });
        Assert.Same(host.Services.GetRequiredService<IDistributedEventTransport>(), host.Services.GetRequiredService<RabbitMqEventTransport>());
        var checks = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Single(checks, x => x.Name == "event-bus-rabbitmq");
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    /// <summary>无效选项按配置键报出，不能推迟到发送时才发现。</summary>
    [Theory]
    [InlineData("Exchange")]
    [InlineData("QueueName")]
    [InlineData("PrefetchCount")]
    [InlineData("ConnectionStringName")]
    public void Invalid_options_report_the_configuration_key(string key)
    {
        using var host = EventTransportTestHost.Create(services => services.AddRabbitMqEventTransport(o =>
        {
            if (key == "Exchange") o.Exchange = "";
            if (key == "QueueName") o.QueueName = "";
            if (key == "PrefetchCount") o.PrefetchCount = 0;
            if (key == "ConnectionStringName") o.ConnectionStringName = "";
        }));
        var error = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<RabbitMqEventTransportOptions>>().Value);
        Assert.Contains("Leistd:EventBus:RabbitMQ:" + key, error.Message);
    }
}
