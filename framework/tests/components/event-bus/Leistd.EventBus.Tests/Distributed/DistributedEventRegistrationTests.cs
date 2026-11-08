using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.EventBus.Tests.Distributed;

public sealed class DistributedEventRegistrationTests
{
    /// <summary>回环重复登记只产生一项健康检查，聚合状态始终健康。</summary>
    [Fact]
    public async Task Repeated_loopback_registration_exposes_one_healthy_event_bus_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLoopbackEventTransport();
        services.AddLoopbackEventTransport();
        services.AddHealthChecks().AddLoopbackEventTransport(["ready"]).AddLoopbackEventTransport(["custom"]);
        using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>()
            .CheckHealthAsync(check => check.Tags.Contains("event-bus"));
        var check = Assert.Single(report.Entries);
        Assert.Contains("event-bus", check.Value.Tags);
        Assert.Contains("ready", check.Value.Tags);
        Assert.Contains("custom", check.Value.Tags);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, report.Status);
    }

    /// <summary>业务写入进程显式关闭发送与接收后不需要传输。</summary>
    [Fact]
    public void Writer_only_process_does_not_require_a_transport()
    {
        var services = Services();
        services.AddDistributedEventBus(options =>
        {
            options.Outbox.DispatchEnabled = false;
            options.Inbox.ReceiveEnabled = false;
            options.Inbox.ProcessingEnabled = false;
        });
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IOptions<DistributedEventOptions>>().Value.Outbox.DispatchEnabled);
    }

    /// <summary>误开分工时在选项校验阶段指出完整配置键。</summary>
    [Fact]
    public void Enabled_roles_without_transport_report_the_bound_configuration_keys()
    {
        var services = Services();
        services.AddDistributedEventBus(configSectionPath: "App:Events");
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DistributedEventOptions>>().Value);
        Assert.Contains("App:Events:Outbox:DispatchEnabled", error.Message);
        Assert.Contains("App:Events:Inbox:ReceiveEnabled", error.Message);
    }

    /// <summary>重复注册不能静默叠加另一个配置节。</summary>
    [Fact]
    public void Registering_a_second_configuration_section_is_rejected()
    {
        var services = Services();
        services.AddDistributedEventBus();
        Assert.Throws<InvalidOperationException>(() => services.AddDistributedEventBus(configSectionPath: "Other:Events"));
    }

    /// <summary>同一入口可重复调用，默认消费者只有一份。</summary>
    [Fact]
    public void Repeated_registration_preserves_one_builtin_subscription()
    {
        var services = Services();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventBus();
        services.AddDistributedEventBus();
        using var provider = services.BuildServiceProvider();
        var subscriptions = provider.GetRequiredService<IDistributedEventSubscriptions>().GetAll();
        Assert.Single(subscriptions, item => item.EventName == DistributedEventTestEvent.Name);
    }

    /// <summary>关闭测试事件时传输订阅表中不出现它。</summary>
    [Fact]
    public void Disabled_test_event_is_absent_from_transport_subscriptions()
    {
        var services = Services();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventBus(options => options.TestEvent.Enabled = false);
        services.AddDistributedEventHandler<OrderHandler>("orders.projection");
        using var provider = services.BuildServiceProvider();
        var subscription = Assert.Single(provider.GetRequiredService<IDistributedEventSubscriptions>().GetAll());
        Assert.Equal("order.paid", subscription.EventName);
        Assert.Equal("orders.projection", subscription.Consumer);
        Assert.Equal(typeof(OrderHandler), subscription.HandlerType);
    }

    /// <summary>相同线上名称不能有两种反序列化类型。</summary>
    [Fact]
    public void Conflicting_event_types_are_rejected_when_building_subscriptions()
    {
        var services = Services();
        services.AddDistributedEventBus();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventHandler<OrderHandler>();
        services.AddDistributedEventHandler<ConflictingHandler>();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDistributedEventSubscriptions>());
    }

    /// <summary>不同处理器不能占用同一事件下同一消费者名。</summary>
    [Fact]
    public void Duplicate_consumer_names_for_an_event_are_rejected()
    {
        var services = Services();
        services.AddDistributedEventBus();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventHandler<OrderHandler>("projection");
        services.AddDistributedEventHandler<SecondOrderHandler>("projection");
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDistributedEventSubscriptions>());
    }

    /// <summary>只实现本地事件接口的类型不能被误登记为分布式消费者。</summary>
    [Fact]
    public void A_handler_without_a_distributed_event_contract_is_rejected()
        => Assert.Throws<ArgumentException>(() => Services().AddDistributedEventHandler<object>());

    /// <summary>退避上限不能低于起点。</summary>
    [Fact]
    public void An_invalid_retry_window_is_rejected()
    {
        var services = Services();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventBus(options => options.RetryMaxDelay = TimeSpan.FromMilliseconds(1));
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DistributedEventOptions>>().Value);
        Assert.Contains("RetryMaxDelay", error.Message);
    }

    /// <summary>上报默认值不需要部署配置；时间范围与实例标识错误指向宿主绑定的完整键。</summary>
    [Theory]
    [InlineData("defaults")]
    [InlineData("zero-interval")]
    [InlineData("timer-overflow")]
    [InlineData("stale-too-short")]
    [InlineData("blank-instance")]
    [InlineData("long-instance")]
    [InlineData("null-options")]
    public void Transport_status_options_validate_defaults_and_report_the_bound_configuration_keys(string scenario)
    {
        var services = Services();
        services.AddLoopbackEventTransport();
        services.AddDistributedEventBus(options =>
        {
            switch (scenario)
            {
                case "zero-interval": options.TransportStatus.ReportInterval = TimeSpan.Zero; break;
                case "timer-overflow": options.TransportStatus.ReportInterval = TimeSpan.MaxValue; break;
                case "stale-too-short": options.TransportStatus.StaleAfter = options.TransportStatus.ReportInterval; break;
                case "blank-instance": options.TransportStatus.Instance = "  "; break;
                case "long-instance": options.TransportStatus.Instance = new string('x', 513); break;
                case "null-options": options.TransportStatus = null!; break;
            }
        }, "App:Events");
        using var provider = services.BuildServiceProvider();
        if (scenario == "defaults")
        {
            var options = provider.GetRequiredService<IOptions<DistributedEventOptions>>().Value.TransportStatus;
            Assert.Equal(TimeSpan.FromSeconds(15), options.ReportInterval);
            Assert.Equal(TimeSpan.FromMinutes(1), options.StaleAfter);
            Assert.Null(options.Instance);
        }
        else
        {
            var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DistributedEventOptions>>().Value);
            var key = scenario switch
            {
                "zero-interval" or "timer-overflow" => ":ReportInterval",
                "stale-too-short" => ":StaleAfter",
                "blank-instance" or "long-instance" => ":Instance",
                _ => ""
            };
            Assert.Contains("App:Events:TransportStatus" + key, error.Message);
        }
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return services;
    }

    [EventName("order.paid")]
    public sealed class OrderPaid : DistributedEvent { }
    [EventName("order.paid")]
    public sealed class ConflictingOrder : DistributedEvent { }
    public sealed class OrderHandler : IEventHandler<OrderPaid>
    {
        public Task HandleAsync(OrderPaid @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    public sealed class SecondOrderHandler : IEventHandler<OrderPaid>
    {
        public Task HandleAsync(OrderPaid @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    public sealed class ConflictingHandler : IEventHandler<ConflictingOrder>
    {
        public Task HandleAsync(ConflictingOrder @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
