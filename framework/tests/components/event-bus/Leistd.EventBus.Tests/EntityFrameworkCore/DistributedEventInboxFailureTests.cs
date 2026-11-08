using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>入站与出站统一持久异常类型，完整异常只留日志。</summary>
public sealed class DistributedEventInboxFailureTests
{
    /// <summary>重试与耗尽都不把处理器异常明文写入 LastError。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Handler_failures_persist_only_the_exception_type(int maxAttempts)
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.MaxAttempts = maxAttempts);
        host.Behavior.Throw = true;
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        await host.Queue.RunAllAsync(host.Services);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(typeof(InvalidOperationException).FullName, row.LastError);
        Assert.Equal(DistributedEventInboxFailureCodes.HandlerException, row.LastErrorCode);
        if (maxAttempts == 1)
            Assert.Contains(host.Logs.GetSnapshot(), r => r.Id.Id == 4103 && r.Exception is InvalidOperationException
                && r.Exception.Message == "The projection could not be processed.");
    }
}
