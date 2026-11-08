using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>D8：真实 SQLite 中两条处理路径及过时并发令牌不得重复提交业务写入。</summary>
public sealed class DistributedEventConcurrencyTests
{
    /// <summary>立即路径与扫描路径同时到达同一行时，只有一份投影提交。</summary>
    [Fact]
    public async Task Concurrent_processor_calls_commit_the_business_projection_only_once()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using (var receive = host.Services.CreateAsyncScope())
            await receive.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        host.Queue.Drop();
        var id = await host.ReadAsync(db => db.Set<DistributedEventInbox>().Select(x => x.Id).SingleAsync());
        async Task ProcessAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DistributedEventInboxProcessor<EventTestDbContext>>().ProcessAsync(null, id, null, default);
        }
        await Task.WhenAll(Task.Run(ProcessAsync), Task.Run(ProcessAsync));
        Assert.Equal(1, (await host.ReadAsync(db => db.Set<EventProjection>().SingleAsync())).Count);
        var row = await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync());
        Assert.Equal(DistributedEventInboxStatus.Processed, row.Status);
        Assert.Equal(1, row.AttemptCount);
    }

    /// <summary>已过时的 AttemptCount 写入触发关系型并发冲突，该事务中的业务写入一起回滚。</summary>
    [Fact]
    public async Task A_stale_attempt_token_rolls_back_its_entire_business_transaction()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        await using (var receive = host.Services.CreateAsyncScope())
            await receive.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(host.Raw(), default);
        host.Queue.Drop();
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<EventTestDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<EventTestDbContext>();
        var winner = await first.Set<DistributedEventInbox>().SingleAsync();
        var stale = await second.Set<DistributedEventInbox>().SingleAsync();
        winner.AttemptCount++; winner.Status = DistributedEventInboxStatus.Processed;
        first.Add(new EventProjection { EventId = winner.EventId, Count = 1 });
        await first.SaveChangesAsync();
        await using var transaction = await second.Database.BeginTransactionAsync();
        second.Add(new EventProjection { EventId = Guid.NewGuid(), Count = 1 });
        stale.AttemptCount++; stale.Status = DistributedEventInboxStatus.Processed;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await transaction.RollbackAsync();
        Assert.Equal(1, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
        Assert.Equal(1, (await host.ReadAsync(db => db.Set<DistributedEventInbox>().SingleAsync())).AttemptCount);
    }
}
