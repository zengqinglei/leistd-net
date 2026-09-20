using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.OperationRecords.EntityFrameworkCore.Options;
using Leistd.OperationRecords.EntityFrameworkCore.Retention;
using Leistd.OperationRecords.Tests.TestDoubles;
using Leistd.TestBase.Doubles;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.OperationRecords.Tests;

/// <summary>
/// 保留期归档：按物理库逐个搬运，同库的其余租户一并覆盖，列逐一对齐。
/// </summary>
/// <remarks>
/// <para>两个错了都不报错的陷阱：无租户上下文里的租户过滤器只放行宿主自己的行（漏 <c>IgnoreQueryFilters</c>
/// 则租户记录永远留着）；只连宿主库则独立库租户的记录永远留着。日志照样打印"归档成功 N 条"。</para>
/// <para>宿主库与租户库是两个独立的 SQLite 内存库，连接按当前租户路由。</para>
/// </remarks>
public sealed class OperationRecordRetentionTests : IAsyncLifetime
{
    private static readonly Guid DedicatedTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SharedTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Now = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

    private readonly string _host = $"Data Source=archive-host-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _tenant = $"Data Source=archive-tenant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _hostAnchor = default!;
    private SqliteConnection _tenantAnchor = default!;
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        _hostAnchor = new SqliteConnection(_host);
        _tenantAnchor = new SqliteConnection(_tenant);
        await _hostAnchor.OpenAsync();
        await _tenantAnchor.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMultiTenancyCore();
        services.AddSingleton<IConnectionStringResolver>(sp => new RoutingResolver(
            sp.GetRequiredService<ICurrentTenantAccessor>(), _host, _tenant));
        services.AddSingleton<ITenantDatabaseEnumerator>(new FixedDatabases(
            TenantDatabase.Host, new TenantDatabase(DedicatedTenant, "dedicated")));
        services.AddSingleton<IClock>(new UtcClockProvider(new FakeTimeProvider(new DateTimeOffset(Now))));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddDbContext<TestDbContext>((_, options) =>
            options.UseSqlite(DbContextCreationContext.Current?.ConnectionString ?? _host));
        services.AddOperationRecordsEfCore<TestDbContext>();
        services.AddOperationRecordRetention<TestDbContext>();
        _services = services.BuildServiceProvider();

        foreach (var connectionString in new[] { _host, _tenant })
        {
            await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options);
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _hostAnchor.DisposeAsync();
        await _tenantAnchor.DisposeAsync();
    }

    [Fact]
    public async Task Expired_records_are_archived_in_every_database_including_other_tenants_sharing_it()
    {
        var expired = Now.AddDays(-400);
        var fresh = Now.AddDays(-1);
        await SeedAsync(_host, (null, expired), (SharedTenant, expired), (null, fresh));
        await SeedAsync(_tenant, (DedicatedTenant, expired), (DedicatedTenant, fresh));

        // 批大小 1：逐批循环走满，每批一个事务
        var result = await _services.GetRequiredService<IOperationRecordArchiveService>()
            .ArchiveOlderThanAsync(Now.AddDays(-365), batchSize: 1);

        Assert.Equal(new OperationRecordArchiveResult(Archived: 3, Databases: 2, FailedDatabases: 0), result);
        Assert.Equal((1, 2), await CountAsync(_host));
        Assert.Equal((1, 1), await CountAsync(_tenant));
    }

    /// <summary>搬运是逐字段复制：原表加了列而归档表没跟上，搬完照样报成功，缺的那列到查归档时才会发现。</summary>
    [Fact]
    public void The_archive_carries_every_record_column()
    {
        var archiveColumns = typeof(OperationRecordArchive).GetProperties().Select(p => p.Name).ToHashSet();

        var missing = typeof(OperationRecord).GetProperties().Select(p => p.Name).Except(archiveColumns);

        Assert.Equal([], missing);
    }

    [Fact]
    public void Retention_is_a_cluster_job_scheduled_daily()
    {
        var definition = _services.GetServices<RecurringJobDefinition>().Single(d => d.Name == "operation-records.archive");

        Assert.Equal(RecurringJobScope.Cluster, definition.Scope);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero),
            definition.ScheduleFactory(_services).GetSlot(new DateTimeOffset(2026, 9, 20, 23, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>越界的保留天数在启动期被拒，而不是某天夜里把最近的记录搬走。</summary>
    [Fact]
    public void An_out_of_range_retention_is_rejected()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddOperationRecordRetention<TestDbContext>(o => o.RetentionDays = 5)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OperationRecordRetentionOptions>>().Value);
    }

    /// <summary>关着照常排期、到点跳过：开关可能被宿主设置随时打开，退出排期的话要重启才生效。</summary>
    [Fact]
    public async Task A_disabled_retention_skips_without_archiving()
    {
        var archive = new CountingArchive(new OperationRecordArchiveResult(0, 1, 0));

        await Job(enabled: false, archive).ExecuteAsync(new RecurringJobContext("operation-records.archive", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(0, archive.Calls);
    }

    /// <summary>有库失败时抛出，让调度器不记水位；失败的库最迟在下一个调度时段重做。</summary>
    [Fact]
    public async Task A_failed_database_fails_the_run()
    {
        var archive = new CountingArchive(new OperationRecordArchiveResult(3, 2, 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Job(enabled: true, archive).ExecuteAsync(new RecurringJobContext("operation-records.archive", DateTimeOffset.UtcNow), CancellationToken.None));
    }

    private static OperationRecordArchiveJob Job(bool enabled, IOperationRecordArchiveService archive) => new(
        new MutableOptionsMonitor<OperationRecordRetentionOptions>(new OperationRecordRetentionOptions { Enabled = enabled }),
        archive,
        new UtcClockProvider(new FakeTimeProvider(new DateTimeOffset(Now))),
        new FakeLogger<OperationRecordArchiveJob>());

    private static async Task SeedAsync(string connectionString, params (Guid? TenantId, DateTime CreationTime)[] rows)
    {
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options);
        db.Set<OperationRecord>().AddRange(rows.Select(row => new OperationRecord
        {
            TenantId = row.TenantId,
            ActorTenantId = row.TenantId,
            Action = "user.created",
            TargetId = "-",
            AuthorizationBasis = "test",
            Outcome = OperationRecordOutcome.Succeeded,
            Visibility = OperationVisibility.Tenant,
            CreationTime = row.CreationTime
        }));
        await db.SaveChangesAsync();
    }

    private static async Task<(int Records, int Archived)> CountAsync(string connectionString)
    {
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options);
        return (
            await db.Set<OperationRecord>().IgnoreQueryFilters().CountAsync(),
            await db.Set<OperationRecordArchive>().CountAsync(a => a.ActorTenantId == a.TenantId));
    }

    private sealed class CountingArchive(OperationRecordArchiveResult result) : IOperationRecordArchiveService
    {
        public int Calls { get; private set; }

        public Task<OperationRecordArchiveResult> ArchiveOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedDatabases(params TenantDatabase[] databases) : ITenantDatabaseEnumerator
    {
        public Task<IReadOnlyList<TenantDatabase>> GetDatabasesAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantDatabase>>(databases);
    }

    private sealed class RoutingResolver(ICurrentTenantAccessor accessor, string host, string tenant) : IConnectionStringResolver
    {
        public Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
            => Task.FromResult(accessor.Current?.TenantId == DedicatedTenant ? tenant : host);
    }
}
