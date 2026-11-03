using Leistd.BackgroundJobs.Queues;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data;
using Leistd.Data.Connections;
using Leistd.Data.Filters;
using Leistd.EventBus.Abstractions;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.TestBase.Doubles;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Publishing;
using Leistd.Webhooks.Signing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Leistd.Webhooks.Tests.TestDoubles;

/// <summary>
/// webhook EF 链路的测试宿主：真实工作单元与多租户上下文、SQLite 文件库（宿主共享库 + 一个专属库）、
/// 可控时钟、脚本化发送器、只记录的队列与事件总线。
/// </summary>
/// <remarks>用文件库而不是共享缓存内存库：扫描有界并发写入，内存共享缓存下并发事务会直接报表锁。</remarks>
public sealed class WebhookTestHost : IAsyncDisposable
{
    /// <summary>共享库里的租户 A。</summary>
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>共享库里的租户 B。</summary>
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>使用专属库的租户。</summary>
    public static readonly Guid DedicatedTenant = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

    /// <summary>用例里发布的业务事件类型。</summary>
    public const string OrderPaid = "order.paid";

    /// <summary>另一个业务事件类型。</summary>
    public const string OrderShipped = "order.shipped";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "leistd-webhooks-" + Guid.NewGuid().ToString("N"));

    private WebhookTestHost(Action<IServiceCollection>? configure, IDictionary<string, string?>? settings, DateTimeOffset? start)
    {
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(_directory);
        HostConnectionString = $"Data Source={Path.Combine(_directory, "host.db")};Pooling=False";
        DedicatedConnectionString = $"Data Source={Path.Combine(_directory, "dedicated.db")};Pooling=False";
        Databases = new FixedDatabases(TenantDatabase.ForHost("host"), new TenantDatabase(DedicatedTenant, "dedicated", []));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddMultiTenancyCore();
        services.AddDataFilters();
        services.AddSingleton<IConnectionStringResolver>(sp => new RoutingResolver(
            sp.GetRequiredService<ICurrentTenantAccessor>(), HostConnectionString, DedicatedConnectionString));
        services.AddSingleton<ITenantDatabaseEnumerator>(Databases);
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddDbContext<WebhookTestDbContext>((_, options)
            => options.UseSqlite(DbContextCreationContext.Current?.ConnectionString ?? HostConnectionString));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ILocalEventBus>(Events);
        services.AddSingleton<IWebhookDefinitionProvider, OrderDefinitions>();
        services.AddWebhooksEfCore<WebhookTestDbContext>();
        services.AddWebhookRetention<WebhookTestDbContext>();
        services.AddSingleton<IBackgroundTaskQueue>(Queue);
        services.AddSingleton<ScriptedWebhookSender>();
        services.AddSingleton<IWebhookSender>(sp => sp.GetRequiredService<ScriptedWebhookSender>());
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        Sender = Services.GetRequiredService<ScriptedWebhookSender>();
    }

    /// <summary>根服务提供器。</summary>
    public ServiceProvider Services { get; }

    /// <summary>可控时钟，默认起点 2026-11-03 00:00 UTC。</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>脚本化发送器。</summary>
    public ScriptedWebhookSender Sender { get; }

    /// <summary>只记录的后台队列。</summary>
    public RecordingTaskQueue Queue { get; } = new();

    /// <summary>只记录的本地事件总线。</summary>
    public RecordingEventBus Events { get; } = new();

    /// <summary>逐库遍历看到的物理库清单。</summary>
    public FixedDatabases Databases { get; }

    /// <summary>宿主共享库连接串。</summary>
    public string HostConnectionString { get; }

    /// <summary>专属库连接串。</summary>
    public string DedicatedConnectionString { get; }

    /// <summary>删掉专属库的文件：之后连上的是没有表的空库，模拟一个库失败。</summary>
    public void BreakDedicatedDatabase()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(_directory, "dedicated.db"));
    }

    /// <summary>当前 UTC 时刻。</summary>
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    /// <summary>建库并返回宿主。</summary>
    /// <param name="configure">在默认注册之后追加或替换服务。</param>
    /// <param name="settings">配置项。</param>
    /// <param name="start">时钟起点；接收方按真实时间校验签名时刻的用例传当前时间。</param>
    public static async Task<WebhookTestHost> CreateAsync(
        Action<IServiceCollection>? configure = null,
        IDictionary<string, string?>? settings = null,
        DateTimeOffset? start = null)
    {
        var host = new WebhookTestHost(configure, settings, start);
        foreach (var connectionString in new[] { host.HostConnectionString, host.DedicatedConnectionString })
        {
            await using var db = host.RawContext(connectionString);
            await db.Database.EnsureCreatedAsync();
        }

        return host;
    }

    /// <summary>在某个租户上下文与工作单元里执行并提交。</summary>
    public async Task<T> InTenantAsync<T>(Guid? tenantId, Func<IServiceProvider, WebhookTestDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        using (provider.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            using var unitOfWork = provider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var dbContext = await provider.GetRequiredService<IDbContextProvider<WebhookTestDbContext>>().GetDbContextAsync();
            var result = await action(provider, dbContext);
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    /// <summary>在某个租户上下文与工作单元里执行并提交。</summary>
    public Task InTenantAsync(Guid? tenantId, Func<IServiceProvider, WebhookTestDbContext, Task> action)
        => InTenantAsync<bool>(tenantId, async (provider, db) =>
        {
            await action(provider, db);
            return true;
        });

    /// <summary>在某个租户上下文里调用管理用例，不套外层工作单元：用例自带的工作单元决定提交。</summary>
    public async Task<T> ManageAsync<T>(Guid? tenantId, Func<IWebhookManagementService, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        using (provider.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            return await action(provider.GetRequiredService<IWebhookManagementService>());
        }
    }

    /// <summary>在某个租户上下文里调用没有返回值的管理用例。</summary>
    public Task ManageAsync(Guid? tenantId, Func<IWebhookManagementService, Task> action)
        => ManageAsync<bool>(tenantId, async service =>
        {
            await action(service);
            return true;
        });

    /// <summary>在某个租户里建一个启用的订阅，返回订阅与明文密钥。</summary>
    public Task<(WebhookSubscription Subscription, string Secret)> AddSubscriptionAsync(
        Guid? tenantId,
        string url = "https://receiver.example/hook",
        params string[] eventTypes)
        => InTenantAsync(tenantId, (provider, db) =>
        {
            var (secret, protectedSecret) = provider.GetRequiredService<WebhookSecretProtector>().CreateSecret();
            var subscription = new WebhookSubscription
            {
                TenantId = tenantId,
                Name = "receiver",
                Url = url,
                EventTypes = eventTypes.Length == 0 ? [OrderPaid] : [.. eventTypes],
                Secret = protectedSecret,
                CreationTime = Now
            };
            db.Add(subscription);
            return Task.FromResult((subscription, secret));
        });

    /// <summary>在某个租户里发布事件并提交。</summary>
    public Task PublishAsync(Guid? tenantId, string eventType = OrderPaid, object? data = null)
        => InTenantAsync(tenantId, (provider, _) =>
            provider.GetRequiredService<IWebhookPublisher>().PublishAsync(eventType, data ?? new { orderId = 42 }));

    /// <summary>执行一轮兜底扫描。</summary>
    public Task RunDispatchAsync() => RunJobAsync("webhooks.dispatch");

    /// <summary>执行一轮保留期清理。</summary>
    public Task RunRetentionAsync() => RunJobAsync("webhooks.retention");

    /// <summary>按名称执行一次已登记的周期任务。</summary>
    public async Task RunJobAsync(string name)
    {
        var definition = Services.GetServices<RecurringJobDefinition>().Single(d => d.Name == name);
        await using var scope = Services.CreateAsyncScope();
        var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);
        await job.ExecuteAsync(new RecurringJobContext(definition.Name, Now), CancellationToken.None);
    }

    /// <summary>不经工作单元、放开租户过滤读某个库（默认宿主共享库）。</summary>
    public async Task<T> ReadAsync<T>(Func<WebhookTestDbContext, Task<T>> read, string? connectionString = null)
    {
        await using var db = RawContext(connectionString ?? HostConnectionString);
        return await read(db);
    }

    /// <summary>读一个库里的全部投递（放开租户过滤）。</summary>
    public Task<List<WebhookDelivery>> DeliveriesAsync(string? connectionString = null)
        => ReadAsync(db => db.Set<WebhookDelivery>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.CreationTime).ToListAsync(), connectionString);

    /// <summary>读一条投递的全部尝试，按时间先后。</summary>
    public Task<List<WebhookAttempt>> AttemptsAsync(Guid deliveryId, string? connectionString = null)
        => ReadAsync(db => db.Set<WebhookAttempt>().AsNoTracking().Where(x => x.DeliveryId == deliveryId)
            .OrderBy(x => x.AttemptedAt).ThenBy(x => x.Id).ToListAsync(), connectionString);

    /// <summary>读一个订阅（放开租户过滤）。</summary>
    public Task<WebhookSubscription> SubscriptionAsync(Guid id, string? connectionString = null)
        => ReadAsync(db => db.Set<WebhookSubscription>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id), connectionString);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响用例结论
        }
    }

    private WebhookTestDbContext RawContext(string connectionString)
        => new(new DbContextOptionsBuilder<WebhookTestDbContext>().UseSqlite(connectionString).Options,
            Services.GetRequiredService<ICurrentTenant>(),
            Services.GetRequiredService<IDataFilter>());

    private sealed class OrderDefinitions : IWebhookDefinitionProvider
    {
        public void Define(IWebhookDefinitionContext context)
        {
            context.Add(OrderPaid, "orders");
            context.Add(OrderShipped, "orders");
        }
    }

    private sealed class RoutingResolver(ICurrentTenantAccessor accessor, string host, string dedicated) : IConnectionStringResolver
    {
        public Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
            => Task.FromResult(accessor.Current?.TenantId == DedicatedTenant ? dedicated : host);
    }
}

/// <summary>固定的物理库清单，可按用例加入解析失败的租户。</summary>
/// <param name="databases">物理库。</param>
public sealed class FixedDatabases(params TenantDatabase[] databases) : ITenantDatabaseEnumerator
{
    /// <summary>本轮解析不出连接的租户。</summary>
    public List<TenantDatabaseFailure> Unresolved { get; } = [];

    /// <inheritdoc />
    public Task<TenantDatabaseSet> GetDatabasesAsync(string name, bool activeOnly, CancellationToken cancellationToken = default)
        => Task.FromResult(new TenantDatabaseSet(databases, [.. Unresolved]));
}
