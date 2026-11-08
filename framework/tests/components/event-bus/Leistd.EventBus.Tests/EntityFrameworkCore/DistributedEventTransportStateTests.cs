using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.TransportStatus;
using Leistd.EventBus.EntityFrameworkCore.Retention;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.MultiTenancy.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>真实宿主库中的跨进程状态、新鲜度、汇总和后台上报。</summary>
public sealed class DistributedEventTransportStateTests
{
    /// <summary>两个独立容器共用数据库；API 无传输，从 Worker 的宿主库上报读取状态。</summary>
    [Fact]
    public async Task A_writer_only_api_reads_worker_reports_and_status_changes_from_the_shared_host_database()
    {
        var check = new MutableCheck();
        await using var worker = await DistributedEventTestHost.CreateAsync(configureServices: services =>
            services.AddHealthChecks().AddCheck("transport", check, tags: ["event-bus"]));
        await using var api = await DistributedEventTestHost.CreateAsync(withTransport: false, shareDatabaseWith: worker,
            configure: WriterOnly);
        var reporter = Reporter(worker);
        await using var workerScope = worker.Services.CreateAsyncScope();
        using var workerTenant = workerScope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.DedicatedTenant);
        await reporter.ReportAsync();
        var first = await worker.ReadAsync(db => db.Set<DistributedEventTransportState>().SingleAsync());
        Assert.Contains($"@{Environment.MachineName}:{Environment.ProcessId}", first.Instance);
        Assert.Equal(0, await worker.ReadAsync(db => db.Set<DistributedEventTransportState>().CountAsync(), dedicated: true));
        await using var scope = api.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        Assert.Equal("healthy", (await management.GetStatusAsync()).TransportStatus);
        worker.Clock.Advance(TimeSpan.FromSeconds(15));
        await reporter.ReportAsync();
        var unchanged = await worker.ReadAsync(db => db.Set<DistributedEventTransportState>().SingleAsync());
        Assert.Equal(first.StatusChangedAt, unchanged.StatusChangedAt);
        Assert.True(unchanged.ReportedAt > first.ReportedAt);
        worker.Clock.Advance(TimeSpan.FromSeconds(15));
        check.Status = HealthStatus.Unhealthy;
        await reporter.ReportAsync();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            var status = await management.GetStatusAsync(DistributedEventTestHost.DedicatedTenant.ToString());
            Assert.Equal("unhealthy", status.TransportStatus); Assert.Equal("scripted", status.TransportName);
            Assert.Equal(worker.Clock.GetUtcNow().UtcDateTime, status.TransportStatusChangedAt);
            Assert.Equal(status.TransportStatusChangedAt, status.TransportReportedAt);
        }
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.DedicatedTenant))
            Assert.Equal("unhealthy", (await management.GetStatusAsync()).TransportStatus);
        Assert.Equal(3, check.Calls); // GET 不执行检查。
    }

    /// <summary>临界时刻仍新鲜，超过时限返回 unknown，两个时间清空。</summary>
    [Fact]
    public async Task Reports_are_fresh_at_the_boundary_and_expire_without_health_checks_on_read()
    {
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: false, configure: WriterOnly);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(host, new DistributedEventTransportState() { Instance = "worker", TransportName = "remote", Status = HealthStatus.Healthy,
            StatusChangedAt = now, ReportedAt = now });
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("healthy", (await management.GetStatusAsync()).TransportStatus);
        host.Clock.Advance(TimeSpan.FromTicks(1));
        var expired = await management.GetStatusAsync();
        Assert.Equal("unknown", expired.TransportStatus); Assert.Null(expired.TransportName);
        Assert.Null(expired.TransportStatusChangedAt); Assert.Null(expired.TransportReportedAt);
    }

    /// <summary>最差状态并列按变化、上报、实例排序；最近上报取所有新鲜实例。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_aggregates_the_worst_fresh_status_with_deterministic_ties(bool localTransport)
    {
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: localTransport,
            configure: localTransport ? null : WriterOnly);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(host,
            new() { Instance = "healthy", TransportName = "healthy", Status = HealthStatus.Healthy, StatusChangedAt = now, ReportedAt = now },
            new() { Instance = "earlier", TransportName = "earlier", Status = HealthStatus.Degraded, StatusChangedAt = now.AddSeconds(-30), ReportedAt = now },
            new() { Instance = "z", TransportName = "z", Status = HealthStatus.Degraded, StatusChangedAt = now.AddSeconds(-10), ReportedAt = now.AddSeconds(-1) },
            new() { Instance = "a", TransportName = "winner", Status = HealthStatus.Degraded, StatusChangedAt = now.AddSeconds(-10), ReportedAt = now.AddSeconds(-1) },
            new() { Instance = "stale", TransportName = "stale", Status = HealthStatus.Unhealthy, StatusChangedAt = now.AddMinutes(-2), ReportedAt = now.AddMinutes(-2) });
        await using var scope = host.Services.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().GetStatusAsync();
        Assert.Equal("degraded", status.TransportStatus);
        Assert.Equal(localTransport ? "scripted" : "winner", status.TransportName);
        Assert.Equal(now.AddSeconds(-10), status.TransportStatusChangedAt);
        Assert.Equal(now, status.TransportReportedAt);
    }

    /// <summary>只执行标签检查，不改全局发布器过滤；没有传输或匹配检查不写数据库。</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reporter_respects_transport_and_tagged_checks_without_changing_host_publishers(bool transport, bool tagged)
    {
        var check = new MutableCheck();
        Func<HealthCheckRegistration, bool> predicate = _ => true;
        await using var host = await DistributedEventTestHost.CreateAsync(withTransport: transport,
            configure: transport ? null : WriterOnly, configureServices: services =>
            {
                services.AddHealthChecks().AddCheck("transport", check, tags: tagged ? ["event-bus"] : [])
                    .AddCheck("unrelated", () => HealthCheckResult.Unhealthy());
                services.Configure<HealthCheckPublisherOptions>(o => o.Predicate = predicate);
            });
        await Reporter(host).ReportAsync();
        var rows = await host.ReadAsync(db => db.Set<DistributedEventTransportState>().ToListAsync());
        Assert.Equal(transport && tagged ? 1 : 0, rows.Count);
        if (rows.Count == 1) Assert.Equal(HealthStatus.Healthy, rows[0].Status);
        Assert.Same(predicate, host.Services.GetRequiredService<IOptions<HealthCheckPublisherOptions>>().Value.Predicate);
        Assert.Equal(transport && tagged ? 1 : 0, check.Calls);
    }

    /// <summary>实际启动后台服务，失败只警告，推进时钟后下一轮恢复写入。</summary>
    [Fact]
    public async Task Background_reporter_retries_failed_checks_at_the_configured_interval()
    {
        var check = new MutableCheck();
        var failure = new OneTimeStatusFailure();
        NotifyingClock clock = null!;
        await using var host = await DistributedEventTestHost.CreateAsync(configure: o => o.TransportStatus.Instance = "worker-explicit",
            configureServices: services =>
            {
                services.AddHealthChecks().AddCheck("transport", check, tags: ["event-bus"]);
                clock = new((TimeProvider)services.Last(x => x.ServiceType == typeof(TimeProvider)).ImplementationInstance!);
                services.AddSingleton<TimeProvider>(clock);
                services.AddDbContext<EventTestDbContext>(o => o.AddInterceptors(failure));
            });
        var reporter = Reporter(host);
        await reporter.StartAsync(default);
        try
        {
            await clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(failure.Thrown);
            Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventTransportState>().CountAsync()));
            Assert.Contains(host.Logs.GetSnapshot(), entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                && entry.Exception is InvalidOperationException);
            check.Status = HealthStatus.Degraded;
            host.Clock.Advance(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(async () => await host.ReadAsync(db => db.Set<DistributedEventTransportState>().AnyAsync(x => x.Status == HealthStatus.Degraded)));
            Assert.Equal("worker-explicit", (await host.ReadAsync(db => db.Set<DistributedEventTransportState>().SingleAsync())).Instance);
        }
        finally { await reporter.StopAsync(default); }
    }

    private static DistributedEventTransportStateReporter<EventTestDbContext> Reporter(DistributedEventTestHost host)
        => Assert.Single(host.Services.GetServices<IHostedService>().OfType<DistributedEventTransportStateReporter<EventTestDbContext>>());

    /// <summary>保留期只删除宿主库过旧报告，不在租户数据库执行状态清理。</summary>
    [Fact]
    public async Task Retention_removes_old_host_reports_and_preserves_current_reports()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(host,
            new() { Instance = "old", TransportName = "scripted", Status = HealthStatus.Healthy, StatusChangedAt = now.AddDays(-20), ReportedAt = now.AddDays(-20) },
            new() { Instance = "active", TransportName = "scripted", Status = HealthStatus.Healthy, StatusChangedAt = now.AddDays(-20), ReportedAt = now });
        await host.InTenantAsync(DistributedEventTestHost.DedicatedTenant, (_, db) =>
        {
            db.Add(new DistributedEventTransportState { Instance = "tenant-sentinel", TransportName = "none", Status = HealthStatus.Healthy,
                StatusChangedAt = now.AddDays(-20), ReportedAt = now.AddDays(-20) });
            return Task.CompletedTask;
        });
        await using var scope = host.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<DistributedEventRetentionJob<EventTestDbContext>>(scope.ServiceProvider)
            .ExecuteAsync(new("event-bus.retention", host.Clock.GetUtcNow()), default);
        Assert.Equal("active", Assert.Single(await host.ReadAsync(db => db.Set<DistributedEventTransportState>().ToListAsync())).Instance);
        Assert.Equal("tenant-sentinel", Assert.Single(await host.ReadAsync(db => db.Set<DistributedEventTransportState>().ToListAsync(), dedicated: true)).Instance);
    }

    private static void WriterOnly(DistributedEventOptions options)
    { options.Outbox.DispatchEnabled = false; options.Inbox.ReceiveEnabled = false; options.Inbox.ProcessingEnabled = false; }

    private static Task SeedAsync(DistributedEventTestHost host, params DistributedEventTransportState[] states)
        => host.InTenantAsync(null, (_, db) => { db.AddRange(states); return Task.CompletedTask; });

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class MutableCheck : IHealthCheck
    {
        public HealthStatus Status { get; set; } = HealthStatus.Healthy;
        public int Calls { get; private set; }
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new HealthCheckResult(Status)); }
    }

    private sealed class NotifyingClock(TimeProvider inner) : TimeProvider
    {
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            Waiting.TrySetResult();
            return timer;
        }
    }

    private sealed class OneTimeStatusFailure : SaveChangesInterceptor
    {
        internal bool Thrown { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Thrown && eventData.Context!.ChangeTracker.Entries<DistributedEventTransportState>().Any())
            { Thrown = true; throw new InvalidOperationException("Status database is unavailable."); }
            return ValueTask.FromResult(result);
        }
    }
}
