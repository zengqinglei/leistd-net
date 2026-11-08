using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>传输抛出的未处理异常只将类型写入记录，完整异常留给日志。</summary>
public sealed class DistributedEventTransportFailureTests
{
    /// <summary>异常消息可能含正文或凭据，管理记录不能持久化该消息。</summary>
    [Fact]
    public async Task Unhandled_transport_failures_persist_only_the_exception_type_and_log_the_exception()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var failure = new InvalidOperationException("Sensitive transport detail.");
        host.Transport.SendFailure = failure;
        await host.InTenantAsync(null, (services, _) => services.GetRequiredService<IDistributedEventBus>().PublishAsync(new PaidEvent()));
        await host.Queue.RunAllAsync(host.Services);

        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().SingleAsync());
        Assert.Equal(typeof(InvalidOperationException).FullName, row.LastError);
        Assert.Equal(DistributedEventOutboxFailureCodes.TransportException, row.LastErrorCode);
        Assert.Contains(host.Logs.GetSnapshot(), record => ReferenceEquals(record.Exception, failure));
    }
}
