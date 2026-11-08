using System.Text;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Leistd.EventBus.Tests.Distributed;

public sealed class LoopbackEventTransportTests
{
    /// <summary>回环也走编码和接收入口，保持事件元数据与不透明坐标。</summary>
    [Fact]
    public async Task Send_round_trips_the_frozen_envelope_through_the_receiver()
    {
        var receiver = new Receiver();
        using var provider = Provider(receiver);
        var transport = provider.GetRequiredService<IDistributedEventTransport>();
        var message = Message();
        var sent = await transport.SendAsync(message, default);
        Assert.Equal(DistributedEventSendStatus.Succeeded, sent.Status);
        var decoded = await transport.DecodeAsync(Assert.IsType<RawDistributedEvent>(receiver.Raw), default);
        Assert.Equal(message.EventId, decoded.EventId);
        Assert.Equal(message.OccurredAt, decoded.OccurredAt);
        Assert.Equal(message.Payload, decoded.Payload);
        Assert.Equal(message.TenantId, decoded.TenantId);
        Assert.Equal("Loopback", decoded.Coordinates["transport"]);
        Assert.Equal(message.Headers, decoded.Headers);
    }

    /// <summary>无法持久接收时发送结果为瞬态失败，不能误报确认成功。</summary>
    [Fact]
    public async Task Persistence_failure_is_reported_as_transient()
    {
        using var provider = Provider(new Receiver { Failure = new InvalidOperationException("Store unavailable.") });
        var result = await provider.GetRequiredService<IDistributedEventTransport>().SendAsync(Message(), default);
        Assert.Equal(DistributedEventSendStatus.TransientFailure, result.Status);
        Assert.Equal("loopback-delivery-failed", result.ErrorCode);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.Error);
    }

    /// <summary>回环将完整异常交给宿主日志，同时发送结果不包含敏感消息。</summary>
    [Fact]
    public async Task Delivery_failure_is_logged_with_the_full_exception_and_a_safe_result()
    {
        var failure = new InvalidOperationException("Sensitive transport detail.");
        using var provider = Provider(new Receiver { Failure = failure }, configure: services => services.AddLogging(builder => builder.AddFakeLogging()));
        var result = await provider.GetRequiredService<IDistributedEventTransport>().SendAsync(Message(), default);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.Error);
        Assert.Contains(provider.GetRequiredService<FakeLogCollector>().GetSnapshot(), entry => ReferenceEquals(entry.Exception, failure));
    }

    /// <summary>坏消息使用稳定拒收原因；取消不被包装为坏消息。</summary>
    [Fact]
    public async Task Malformed_envelope_is_poison_but_cancellation_propagates()
    {
        using var provider = Provider(new Receiver());
        var transport = provider.GetRequiredService<IDistributedEventTransport>();
        var raw = new RawDistributedEvent { Body = Encoding.UTF8.GetBytes("{broken:"), Headers = new Dictionary<string, string>(), Coordinates = new Dictionary<string, string>(), ReceivedAt = DateTime.UtcNow };
        var failure = await Assert.ThrowsAsync<DistributedEventDecodeException>(() => transport.DecodeAsync(raw, default).AsTask());
        Assert.Equal(DistributedEventRejectionReasons.InvalidEnvelope, failure.ReasonCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.DecodeAsync(raw, cancellation.Token).AsTask());
    }

    /// <summary>显式禁用接收时，回环同样不调用持久接收入口。</summary>
    [Fact]
    public async Task Receive_disabled_does_not_call_the_loopback_receiver()
    {
        var receiver = new Receiver();
        using var provider = Provider(receiver, receiveEnabled: false);
        var result = await provider.GetRequiredService<IDistributedEventTransport>().SendAsync(Message(), default);
        Assert.Equal(DistributedEventSendStatus.Succeeded, result.Status);
        Assert.Null(receiver.Raw);
    }

    /// <summary>回环发送在进入接收入口之前传播取消，包括禁用接收的进程。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Canceled_send_never_invokes_the_receiver(bool enabled)
    {
        var receiver = new Receiver();
        using var provider = Provider(receiver, enabled);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<IDistributedEventTransport>().SendAsync(Message(), cancellation.Token));
        Assert.Null(receiver.Raw);
    }

    private static ServiceProvider Provider(Receiver receiver, bool receiveEnabled = true, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddOptions<DistributedEventOptions>().Configure(options => options.Inbox.ReceiveEnabled = receiveEnabled);
        services.AddLoopbackEventTransport();
        services.AddScoped<IDistributedEventReceiver>(_ => receiver);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static OutgoingDistributedEvent Message() => new()
    {
        EventId = Guid.CreateVersion7(), EventName = "order.paid", OccurredAt = new DateTime(2026, 11, 8, 1, 2, 3, DateTimeKind.Utc),
        TenantId = Guid.NewGuid(), OrderingKey = "order:1", Payload = "{\"amount\":12}",
        Headers = new Dictionary<string, string> { ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" }
    };

    private sealed class Receiver : IDistributedEventReceiver
    {
        public RawDistributedEvent? Raw { get; private set; }
        public Exception? Failure { get; init; }
        public Task ReceiveAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
        {
            Raw = raw;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
        public Task RejectAsync(RawDistributedEvent raw, string reasonCode, string error, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
