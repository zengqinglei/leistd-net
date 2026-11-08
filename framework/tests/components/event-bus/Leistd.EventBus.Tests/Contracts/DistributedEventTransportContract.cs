using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Leistd.EventBus.Tests.Contracts;

/// <summary>D11：两种真实 broker 复用持久接收与确认时机的契约；无显式测试连接时跳过。</summary>
public abstract class DistributedEventTransportContract
{
    /// <summary>被测传输。</summary>
    protected abstract string TransportKind { get; }

    /// <summary>发送的原始元数据、排序键、消息头与坐标由接收边界拿到。</summary>
    [SkippableFact]
    public async Task A_confirmed_publication_reaches_the_receiver_with_original_metadata_and_coordinates()
    {
        await using var fixture = await CreateAsync();
        var message = BrokerContractFixture.Message();
        Assert.Equal(DistributedEventSendStatus.Succeeded, (await fixture.Transport.SendAsync(message, default)).Status);
        await BrokerContractFixture.WaitAsync(() => fixture.State.Persisted.Count >= 1);
        var received = Assert.Single(fixture.State.Persisted);
        Assert.Equal(message.EventId, received.EventId);
        Assert.Equal(message.OccurredAt, received.OccurredAt);
        Assert.Equal(message.OrderingKey, received.OrderingKey);
        Assert.Equal(message.Headers, received.Headers);
        Assert.NotEmpty(received.Coordinates);
    }

    /// <summary>持久写入失败不确认；重启相同队列／组后仍从第一条消息接收，第二条不能越过它。</summary>
    [SkippableFact]
    public async Task Persistence_failure_leaves_the_message_unconfirmed_and_restart_redelivers_it_before_the_next_message()
    {
        await using var fixture = await CreateAsync();
        var first = BrokerContractFixture.Message();
        var second = BrokerContractFixture.Message();
        fixture.State.Failures.TryAdd(first.EventId, 0);
        Assert.Equal(DistributedEventSendStatus.Succeeded, (await fixture.Transport.SendAsync(first, default)).Status);
        Assert.Equal(DistributedEventSendStatus.Succeeded, (await fixture.Transport.SendAsync(second, default)).Status);
        await BrokerContractFixture.WaitAsync(() => fixture.State.Attempts.GetValueOrDefault(first.EventId) >= 2);
        Assert.Empty(fixture.State.Persisted);
        Assert.False(fixture.State.Attempts.ContainsKey(second.EventId));
        await fixture.RestartAsync();
        await BrokerContractFixture.WaitAsync(() => fixture.State.Persisted.Count >= 2);
        Assert.Equal(new[] { first.EventId, second.EventId }, fixture.State.Persisted.Select(x => x.EventId));
    }

    /// <summary>坏消息的拒收未持久成功时也不能确认，成功后才允许后续消息进入。</summary>
    [SkippableFact]
    public async Task A_bad_message_is_confirmed_only_after_rejection_persists_and_does_not_block_later_messages()
    {
        await using var fixture = await CreateAsync();
        fixture.State.FailRejections = true;
        await fixture.PublishRawAsync(new byte[] { 1, 2, 3 });
        var next = BrokerContractFixture.Message();
        Assert.Equal(DistributedEventSendStatus.Succeeded, (await fixture.Transport.SendAsync(next, default)).Status);
        await BrokerContractFixture.WaitAsync(() => fixture.State.RejectionAttempts >= 2);
        Assert.Empty(fixture.State.Persisted);
        fixture.State.FailRejections = false;
        await BrokerContractFixture.WaitAsync(() => fixture.State.Persisted.Count >= 1);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(fixture.State.Rejections));
        Assert.Equal(next.EventId, Assert.Single(fixture.State.Persisted).EventId);
    }

    /// <summary>停止接收连接后健康变为 Unhealthy，不继续报告上一连接的绿色状态。</summary>
    [SkippableFact]
    public async Task Stopping_the_consumer_changes_transport_health_to_unhealthy()
    {
        await using var fixture = await CreateAsync();
        Assert.Equal(HealthStatus.Healthy, await fixture.HealthAsync());
        await fixture.StopAsync();
        Assert.Equal(HealthStatus.Unhealthy, await fixture.HealthAsync());
    }

    /// <summary>显式关闭 ReceiveEnabled 后即使队列／主题已有消息也不启动消费。</summary>
    [SkippableFact]
    public async Task A_receive_disabled_process_does_not_start_a_consumer()
    {
        await using var fixture = await CreateAsync(receiveEnabled: false);
        Assert.Equal(DistributedEventSendStatus.Succeeded, (await fixture.Transport.SendAsync(BrokerContractFixture.Message(), default)).Status);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.Empty(fixture.State.Persisted);
        Assert.Empty(fixture.State.Attempts);
    }

    /// <summary>创建当前传输的独立真实 broker 夹具。</summary>
    private protected async Task<BrokerContractFixture> CreateAsync(bool receiveEnabled = true)
    {
        var key = TransportKind == "rabbitmq" ? "LEISTD_TEST_RABBITMQ" : "LEISTD_TEST_KAFKA";
        var connection = Environment.GetEnvironmentVariable(key);
        Skip.If(string.IsNullOrWhiteSpace(connection), $"Set {key} to run the real broker contract.");
        return await BrokerContractFixture.CreateAsync(TransportKind, connection!, receiveEnabled);
    }
}
