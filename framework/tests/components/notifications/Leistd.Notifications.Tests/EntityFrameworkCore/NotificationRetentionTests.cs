using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Notifications.EntityFrameworkCore;
using Leistd.Notifications.EntityFrameworkCore.Entities;
using Leistd.Notifications.EntityFrameworkCore.Options;
using Leistd.TestBase.Doubles;
using Leistd.Timing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Notifications.Tests.EntityFrameworkCore;

/// <summary>
/// 通知保留期：已读与未读分开计时，按物理库逐个清理，同库的其余租户一并覆盖。
/// </summary>
public sealed class NotificationRetentionTests : IAsyncLifetime
{
    private static readonly Guid DedicatedTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 9, 20, 19, 0, 0, DateTimeKind.Utc);

    private readonly string _host = $"Data Source=notify-host-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly string _tenant = $"Data Source=notify-tenant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        services.AddSingleton<IConnectionStringResolver>(sp => new RoutingResolver(sp.GetRequiredService<ICurrentTenantAccessor>(), _host, _tenant));
        services.AddSingleton<ITenantDatabaseEnumerator>(new FixedDatabases(TenantDatabase.Host, new TenantDatabase(DedicatedTenant, "dedicated")));
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddDbContext<RetentionDbContext>((_, options) => options.UseSqlite(DbContextCreationContext.Current?.ConnectionString ?? _host));
        services.AddNotificationsEfCore<RetentionDbContext>();
        services.AddNotificationRetention<RetentionDbContext>();
        _services = services.BuildServiceProvider();

        foreach (var connectionString in new[] { _host, _tenant })
        {
            await using var db = new RetentionDbContext(new DbContextOptionsBuilder<RetentionDbContext>().UseSqlite(connectionString).Options);
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _hostAnchor.DisposeAsync();
        await _tenantAnchor.DisposeAsync();
    }

    /// <summary>
    /// 已读超 90 天删、未读超 365 天删，未读的在两者之间保留；独立库与共享库里的其他租户都被覆盖
    /// </summary>
    [Fact]
    public async Task Expired_notifications_are_deleted_per_database_by_read_state()
    {
        await SeedAsync(_host,
            (null, "read-old", true, 100),
            (Guid.NewGuid(), "shared-tenant-read-old", true, 100),
            (null, "unread-mid", false, 100),
            (null, "unread-old", false, 400),
            (null, "read-new", true, 10));
        await SeedAsync(_tenant,
            (DedicatedTenant, "tenant-read-old", true, 100),
            (DedicatedTenant, "tenant-unread-mid", false, 100));

        var definition = _services.GetServices<RecurringJobDefinition>().Single(d => d.Name == "notifications.retention");
        await using (var scope = _services.CreateAsyncScope())
        {
            var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);
            await job.ExecuteAsync(new RecurringJobContext(definition.Name, Now), CancellationToken.None);
        }

        Assert.Equal(["read-new", "unread-mid"], await TitlesAsync(_host));
        Assert.Equal(["tenant-unread-mid"], await TitlesAsync(_tenant));
        Assert.Equal(RecurringJobScope.Cluster, definition.Scope);
    }

    /// <summary>未读比已读先删就本末倒置：用户还没看到的通知反而先消失。</summary>
    [Fact]
    public void An_unread_retention_shorter_than_read_is_rejected()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddNotificationRetention<RetentionDbContext>(o => o.UnreadRetentionDays = 30)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NotificationRetentionOptions>>().Value);
    }

    private static async Task SeedAsync(string connectionString, params (Guid? TenantId, string Title, bool IsRead, int AgeDays)[] rows)
    {
        await using var db = new RetentionDbContext(new DbContextOptionsBuilder<RetentionDbContext>().UseSqlite(connectionString).Options);
        db.Set<NotificationRecord>().AddRange(rows.Select(row => new NotificationRecord
        {
            TenantId = row.TenantId,
            UserId = "u1",
            Title = row.Title,
            IsRead = row.IsRead,
            CreationTime = Now.AddDays(-row.AgeDays)
        }));
        await db.SaveChangesAsync();
    }

    private static async Task<string[]> TitlesAsync(string connectionString)
    {
        await using var db = new RetentionDbContext(new DbContextOptionsBuilder<RetentionDbContext>().UseSqlite(connectionString).Options);
        return await db.Set<NotificationRecord>().IgnoreQueryFilters().OrderBy(x => x.Title).Select(x => x.Title).ToArrayAsync();
    }

    public sealed class RetentionDbContext(DbContextOptions<RetentionDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureNotifications();
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
