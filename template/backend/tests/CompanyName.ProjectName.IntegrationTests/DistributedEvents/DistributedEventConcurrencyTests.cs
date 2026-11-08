#if (LocalIdentity)
using System.Text.Json;
using Leistd.BackgroundJobs.Queues;
using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Leistd.Settings.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests.DistributedEvents;

/// <summary>D8：PostgreSQL 中两条真实处理路径都读到 Pending 后竞争提交。</summary>
public sealed class DistributedEventConcurrencyTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    /// <summary>屏障确保两次进入处理器；收件箱并发令牌使败者事务中的业务投影一起回滚。</summary>
    [Fact]
    public async Task Two_pending_snapshots_commit_only_one_business_write_and_one_processed_transition()
    {
        var gate = new ConcurrentProcessingGate();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(gate);
            services.AddDistributedEventHandler<ConcurrentProjectionHandler>("tests.concurrent-projection");
            services.Replace(ServiceDescriptor.Singleton<IBackgroundTaskQueue, DroppedEventQueue>());
        }));
        // 启动初始化与播种；测试只控制分布式事件的立即队列，不绕过应用组合。
        using var client = host.CreateClient();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDistributedEventReceiver>().ReceiveAsync(new()
            {
                Body = JsonSerializer.SerializeToUtf8Bytes(new OutgoingDistributedEvent
                {
                    EventId = id, EventName = "tests.concurrent", OccurredAt = now, Payload = "{}",
                    Headers = new Dictionary<string, string>()
                }, JsonSerializerOptions.Web),
                Headers = new Dictionary<string, string>(), Coordinates = new Dictionary<string, string>(), ReceivedAt = now
            }, default);
            using var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<MyProjectDbContext>>().GetDbContextAsync();
            var row = await db.Set<DistributedEventInbox>().SingleAsync(row => row.EventId == id);
            row.NextAttemptAt = now.AddSeconds(-1);
            await unit.CompleteAsync();
        }
        async Task ScanAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var definition = scope.ServiceProvider.GetServices<RecurringJobDefinition>().Single(job => job.Name == "event-bus.inbox");
            var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);
            await job.ExecuteAsync(new(definition.Name, DateTimeOffset.UtcNow), default);
        }
        await Task.WhenAll(Task.Run(ScanAsync), Task.Run(ScanAsync)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, gate.Arrivals);
        await using var read = host.Services.CreateAsyncScope();
        var context = read.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        Assert.Equal(1, await context.Set<SettingRecord>().CountAsync(row => row.Name == "tests.concurrent." + id));
        var processed = await context.Set<DistributedEventInbox>().SingleAsync(row => row.EventId == id);
        Assert.Equal(DistributedEventInboxStatus.Processed, processed.Status);
        Assert.Equal(1, processed.AttemptCount);
        Assert.Null(processed.LastErrorCode);
    }
}

internal sealed class ConcurrentProcessingGate
{
    private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;
    public int Arrivals => Volatile.Read(ref _arrivals);
    public Task EnterAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _arrivals) == 2) _bothRead.SetResult();
        return _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
    }
}

[EventName("tests.concurrent")]
internal sealed class ConcurrentProjectionEvent : DistributedEvent;

internal sealed class ConcurrentProjectionHandler(IDbContextProvider<MyProjectDbContext> contexts, ConcurrentProcessingGate gate)
    : IEventHandler<ConcurrentProjectionEvent>
{
    public async Task HandleAsync(ConcurrentProjectionEvent @event, CancellationToken cancellationToken = default)
    {
        // 两路已经在独立事务里读过同一 Pending 行，先会合，再写彼此不冲突的业务数据。
        await gate.EnterAsync(cancellationToken);
        var db = await contexts.GetDbContextAsync(cancellationToken);
        db.Add(new SettingRecord { ScopeKey = "tests/" + Guid.NewGuid(), Name = "tests.concurrent." + @event.EventId, Value = "1" });
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class DroppedEventQueue : IBackgroundTaskQueue
{
    public bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem) => false;
    public ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
#endif
