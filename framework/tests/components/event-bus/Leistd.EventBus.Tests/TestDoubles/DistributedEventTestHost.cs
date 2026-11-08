using System.Diagnostics;
using Leistd.BackgroundJobs.Queues;
using Leistd.Data.Connections;
using Leistd.Data.Filters;
using Leistd.Data;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;
using Leistd.EventBus.Local;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Tenancy;
using Leistd.MultiTenancy;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Leistd.EventBus.Tests.TestDoubles;

/// <summary>真实 SQLite 与工作单元测试宿主；队列由用例显式驱动。</summary>
public sealed class DistributedEventTestHost : IAsyncDisposable
{
    /// <summary>共享库租户。</summary>
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    /// <summary>另一个共享库租户。</summary>
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    /// <summary>专属库租户。</summary>
    public static readonly Guid DedicatedTenant = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
    private readonly string _file = Path.Combine(Path.GetTempPath(), "leistd-events-" + Guid.NewGuid().ToString("N") + ".db");

    private string DedicatedFile => _file + ".dedicated";
    private string Connection(bool dedicated) => $"Data Source={(dedicated ? DedicatedFile : _file)};Pooling=False";

    /// <summary>模拟专属库连接可用、表丢失。</summary>
    public void BreakDedicatedDatabase() => File.Delete(DedicatedFile);

    /// <summary>根容器。</summary>
    public ServiceProvider Services { get; private set; } = null!;
    /// <summary>可控时间。</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
    /// <summary>真实链路日志。</summary>
    public FakeLogCollector Logs => Services.GetRequiredService<FakeLogCollector>();
    /// <summary>记录的队列。</summary>
    public EventTaskQueue Queue { get; } = new();
    /// <summary>脚本化消费者行为。</summary>
    public EventHandlerBehavior Behavior { get; } = new();
    /// <summary>已提交管理动作。</summary>
    public List<DistributedEventManagedEvent> ManagedEvents { get; } = [];
    /// <summary>动作提交时所属租户。</summary>
    public List<Guid?> ManagedEventTenants { get; } = [];
    /// <summary>可控传输。</summary>
    public ScriptedEventTransport Transport { get; } = new();

    /// <summary>创建并建表。</summary>
    public static async Task<DistributedEventTestHost> CreateAsync(bool loopback = false, Action<DistributedEventOptions>? configure = null, bool withTransport = true)
    {
        var host = new DistributedEventTestHost();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddFakeLogging());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLocalEventBus();
        services.AddSingleton<IEventHandler<DistributedEventManagedEvent>>(sp => new ManagedEventRecorder(host, sp.GetRequiredService<ICurrentTenant>()));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddMultiTenancyCore();
        services.AddDataFilters();
        services.AddSingleton<IConnectionStringResolver>(sp => new EventRoutingResolver(sp.GetRequiredService<ICurrentTenantAccessor>(), host.Connection(false), host.Connection(true)));
        services.AddSingleton<ITenantDatabaseEnumerator, EventDatabaseDirectory>();
        services.AddCorrelationIdCore();
        services.AddSingleton<TimeProvider>(host.Clock);
        services.AddSingleton<IBackgroundTaskQueue>(host.Queue);
        services.AddSingleton(host.Behavior);
        services.AddSingleton<ITenantStore, EventTenantStore>();
        services.AddDbContext<EventTestDbContext>(options => options.UseSqlite(DbContextCreationContext.Current?.ConnectionString ?? host.Connection(false)));
        services.AddDistributedEventBus(configure);
        services.AddDistributedEventHandler<PaidHandler>("orders.projection");
        services.AddDistributedEventBusEfCore<EventTestDbContext>();
        services.AddDistributedEventRetention<EventTestDbContext>();
        if (loopback) services.AddLoopbackEventTransport();
        else if (withTransport) services.AddSingleton<IDistributedEventTransport>(host.Transport);
        host.Services = services.BuildServiceProvider();
        await using var scope = host.Services.CreateAsyncScope();
        foreach (var dedicated in new[] { false, true })
        {
            await using var db = host.RawContext(scope.ServiceProvider, dedicated);
            await db.Database.EnsureCreatedAsync();
        }
        return host;
    }

    /// <summary>在指定租户与真实事务中执行并提交。</summary>
    public async Task InTenantAsync(Guid? tenantId, Func<IServiceProvider, EventTestDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
        using var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<EventTestDbContext>>().GetDbContextAsync();
        await action(scope.ServiceProvider, db);
        await unit.CompleteAsync();
    }

    /// <summary>读取物理库中全部行，不依赖调用方租户。</summary>
    public async Task<T> ReadAsync<T>(Func<EventTestDbContext, Task<T>> read, bool dedicated = false)
    {
        await using var scope = Services.CreateAsyncScope();
        await using var db = RawContext(scope.ServiceProvider, dedicated);
        return await read(db);
    }

    private EventTestDbContext RawContext(IServiceProvider services, bool dedicated) => new(
        new DbContextOptionsBuilder<EventTestDbContext>().UseSqlite(Connection(dedicated)).Options,
        services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IDataFilter>());

    /// <summary>构造接收消息；载荷可不携带框架元数据。</summary>
    public RawDistributedEvent Raw(Guid? tenant = null, string payload = "{\"orderId\":7}", Guid? id = null) => new()
    {
        Body = DistributedEventCodec.Encode(new()
        {
            EventId = id ?? Guid.NewGuid(), EventName = "order.paid", TenantId = tenant,
            OccurredAt = Clock.GetUtcNow().UtcDateTime, Payload = payload, Headers = new Dictionary<string, string>()
        }),
        Headers = new Dictionary<string, string>(), Coordinates = new Dictionary<string, string> { ["topic"] = "order.paid" },
        ReceivedAt = Clock.GetUtcNow().UtcDateTime
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        File.Delete(_file);
        File.Delete(DedicatedFile);
    }
}

/// <summary>映射真实三表，使用与基座一致的租户过滤。</summary>
public sealed class EventTestDbContext(DbContextOptions<EventTestDbContext> options, ICurrentTenant tenant, IDataFilter filters) : DbContext(options)
{
    private Guid? TenantId => tenant.Id;
    private bool FilterEnabled => filters.IsEnabled<IMultiTenant>();
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureDistributedEventBus();
        model.Entity<DistributedEventOutbox>().HasQueryFilter(x => !FilterEnabled || x.TenantId == TenantId);
        model.Entity<DistributedEventInbox>().HasQueryFilter(x => !FilterEnabled || x.TenantId == TenantId);
        model.Entity<EventProjection>().HasKey(x => x.EventId);
    }
}

/// <summary>测试事件。</summary>
[EventName("order.paid")]
public sealed class PaidEvent : DistributedEvent, IOrderedDistributedEvent
{
    /// <summary>业务标识。</summary>
    public int OrderId { get; set; }
    /// <inheritdoc />
    public string OrderingKey => "order:" + OrderId;
}

/// <summary>处理器事务内写入的投影。</summary>
public sealed class EventProjection
{
    /// <summary>事件标识。</summary>
    public Guid EventId { get; set; }
    /// <summary>处理次数。</summary>
    public int Count { get; set; }
}

/// <summary>处理结果控制与上下文观测。</summary>
public sealed class EventHandlerBehavior
{
    /// <summary>提交前模拟处理失败。</summary>
    public bool Throw { get; set; }
    /// <summary>处理时观测到的事件。</summary>
    public PaidEvent? Event { get; set; }
    /// <summary>处理时观测到的租户。</summary>
    public Guid? Tenant { get; set; }
    /// <summary>处理时链路。</summary>
    public ActivityTraceId TraceId { get; set; }
    /// <summary>处理时关联标识。</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>在业务事务内维护投影。</summary>
public sealed class PaidHandler(IDbContextProvider<EventTestDbContext> contexts, EventHandlerBehavior behavior,
    ICurrentTenant tenant, ICorrelationIdProvider correlation) : IEventHandler<PaidEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(PaidEvent @event, CancellationToken cancellationToken = default)
    {
        behavior.Event = @event; behavior.Tenant = tenant.Id; behavior.TraceId = Activity.Current?.TraceId ?? default;
        behavior.CorrelationId = correlation.Get();
        var db = await contexts.GetDbContextAsync(cancellationToken);
        var projection = await db.Set<EventProjection>().FindAsync([@event.EventId], cancellationToken);
        if (projection is null) db.Add(new EventProjection { EventId = @event.EventId, Count = 1 });
        else projection.Count++;
        await db.SaveChangesAsync(cancellationToken);
        if (behavior.Throw) throw new InvalidOperationException("The projection could not be processed.");
    }
}

/// <summary>只接受登记测试租户的目录。</summary>
public sealed class EventTenantStore : ITenantStore
{
    /// <inheritdoc />
    public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult((id == DistributedEventTestHost.TenantA || id == DistributedEventTestHost.TenantB || id == DistributedEventTestHost.DedicatedTenant) ? new TenantConfiguration { Id = id, Name = "portal", NormalizedName = "PORTAL" } : null);
    /// <inheritdoc />
    public Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
        => Task.FromResult<TenantConfiguration?>(null);
}

/// <summary>可按调用脚本返回成功、瞬态或永久失败的传输。</summary>
public sealed class ScriptedEventTransport : IDistributedEventTransport
{
    /// <inheritdoc />
    public string Name => "scripted";
    /// <summary>发送的事件。</summary>
    public System.Collections.Concurrent.ConcurrentQueue<OutgoingDistributedEvent> Sent { get; } = new();
    /// <summary>发送结果脚本。</summary>
    public System.Collections.Concurrent.ConcurrentQueue<DistributedEventSendResult> Results { get; } = new();
    /// <inheritdoc />
    public Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.FromResult(Results.TryDequeue(out var result) ? result : new DistributedEventSendResult { Status = DistributedEventSendStatus.Succeeded });
    }
    /// <summary>外部格式映射脚本。</summary>
    public Func<RawDistributedEvent, CancellationToken, ValueTask<IncomingDistributedEvent>>? Decoder { get; set; }
    /// <inheritdoc />
    public ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
        => Decoder is null ? ValueTask.FromResult(DistributedEventCodec.Decode(raw)) : Decoder(raw, cancellationToken);
}

/// <summary>由用例驱动的进程内队列；不继承入队请求上下文。</summary>
public sealed class EventTaskQueue : IBackgroundTaskQueue
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Func<IServiceProvider, CancellationToken, ValueTask>> _items = new();
    /// <summary>入队项数。</summary>
    public int Count => _items.Count;
    /// <summary>模拟队列满。</summary>
    public bool Accepts { get; set; } = true;
    /// <inheritdoc />
    public bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    { if (Accepts) _items.Enqueue(workItem); return Accepts; }
    /// <inheritdoc />
    public ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem, CancellationToken cancellationToken = default)
    { _items.Enqueue(workItem); return ValueTask.CompletedTask; }
    /// <summary>依次执行全部项，包括执行期间新入队的接收处理。</summary>
    public async Task RunAllAsync(IServiceProvider services)
    {
        while (_items.TryDequeue(out var item))
        {
            await using var scope = services.CreateAsyncScope();
            await item(scope.ServiceProvider, CancellationToken.None);
        }
    }
    /// <summary>丢弃立即路径，模拟进程退出。</summary>
    public void Drop() => _items.Clear();
}

internal sealed class ManagedEventRecorder(DistributedEventTestHost host, ICurrentTenant tenant) : IEventHandler<DistributedEventManagedEvent>
{
    public Task HandleAsync(DistributedEventManagedEvent @event, CancellationToken cancellationToken = default)
    {
        if (Leistd.UnitOfWork.Events.UnitOfWorkContext.CurrentPhase == Leistd.UnitOfWork.Events.UnitOfWorkPhase.AfterCommit)
        { host.ManagedEvents.Add(@event); host.ManagedEventTenants.Add(tenant.Id); }
        return Task.CompletedTask;
    }
}

internal sealed class EventRoutingResolver(ICurrentTenantAccessor accessor, string shared, string dedicated) : IConnectionStringResolver
{
    public Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
        => Task.FromResult(accessor.Current?.TenantId == DistributedEventTestHost.DedicatedTenant ? dedicated : shared);
}

internal sealed class EventDatabaseDirectory : ITenantDatabaseEnumerator
{
    public Task<TenantDatabaseSet> GetDatabasesAsync(string name, bool activeOnly, CancellationToken cancellationToken = default)
        => Task.FromResult(new TenantDatabaseSet([
            TenantDatabase.ForHost("host"),
            new(DistributedEventTestHost.DedicatedTenant, "dedicated", [DistributedEventTestHost.DedicatedTenant])
        ], []));
}
