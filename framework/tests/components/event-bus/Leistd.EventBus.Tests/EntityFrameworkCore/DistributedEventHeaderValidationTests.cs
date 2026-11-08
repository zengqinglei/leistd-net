using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>不采信非法关联标识列，同时保留消息头，避免新增列导致消息无法确认。</summary>
public sealed class DistributedEventHeaderValidationTests
{
    /// <summary>三处写入均接受长度上限，超长时只留原始头，接收和处理仍成功。</summary>
    [Theory]
    [InlineData("outbox", false)]
    [InlineData("inbox", false)]
    [InlineData("rejection", false)]
    [InlineData("outbox", true)]
    [InlineData("inbox", true)]
    [InlineData("rejection", true)]
    public async Task Correlation_column_accepts_the_limit_and_omits_oversized_values_without_changing_headers(string kind, bool valid)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var value = new string('x', CorrelationIdConstants.MaxLength + (valid ? 0 : 1));
        var headers = new Dictionary<string, string> { [DistributedEventHeaders.CorrelationId] = value };
        await using var scope = host.Services.CreateAsyncScope();
        var receiver = scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>();
        string? stored;
        Dictionary<string, string> frozen;
        if (kind == "outbox")
        {
            await host.InTenantAsync(null, async (sp, _) =>
            {
                using var correlation = sp.GetRequiredService<ICorrelationIdProvider>().Change(value);
                await sp.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent());
            });
            var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
            stored = row.CorrelationId; frozen = row.Headers;
        }
        else if (kind == "inbox")
        {
            await receiver.ReceiveAsync(host.Raw(headers: headers), default);
            await host.Queue.RunAllAsync(host.Services);
            var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
            Assert.Equal(DistributedEventInboxStatus.Processed, row.Status);
            stored = row.CorrelationId; frozen = row.Headers;
        }
        else
        {
            await receiver.ReceiveAsync(host.Raw(headers: headers) with { Body = new byte[] { 1, 2 } }, default);
            var row = await host.ReadAsync(db => db.Set<DistributedEventRejection>().SingleAsync());
            stored = row.CorrelationId; frozen = row.Headers;
        }
        Assert.Equal(value, frozen[DistributedEventHeaders.CorrelationId]);
        Assert.Equal(valid ? value : null, stored);
    }

    /// <summary>存储按与处理器一致的精确消息头名称提取链路，不单独采信其他大小写。</summary>
    [Theory]
    [InlineData("traceparent", true)]
    [InlineData("TraceParent", false)]
    public async Task Trace_column_uses_the_same_header_name_as_the_consumer_context(string header, bool recognized)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        const string trace = "abcdef0123456789abcdef0123456789";
        var raw = host.Raw(headers: new Dictionary<string, string> { [header] = "00-" + trace + "-1234567890abcdef-01" });
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(raw, default);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(recognized ? trace : null, row.TraceId);
    }
}
