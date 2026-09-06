using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Tests.EntityFrameworkCore;

public class TenantConnectionConfigurationStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private ServiceProvider _provider = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddMultiTenancyEfCore<TestDbContext>();
        _provider = services.BuildServiceProvider();

        await _provider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Reads_control_database_record_independently_of_current_tenant()
    {
        var tenants = _provider.GetRequiredService<ITenantManager>();
        var manager = _provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var store = _provider.GetRequiredService<ITenantConnectionConfigurationStore>();
        var currentTenant = _provider.GetRequiredService<ICurrentTenant>();

        var target = await tenants.CreateAsync("target", null, isActive: false);
        var other = await tenants.CreateAsync("other", null, isActive: false);
        await manager.SetAsync(target.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        using (currentTenant.Change(other.Id, other.Name))
        {
            var configuration = await store.FindAsync(target.Id);
            Assert.NotNull(configuration);
            Assert.Equal(target.Id, configuration.TenantId);
        }
    }

    [Fact]
    public void Tenant_configuration_does_not_gain_connection_or_secret_fields()
    {
        var propertyNames = typeof(TenantConfiguration).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Connection", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_returns_control_records_for_migration_target_enumeration()
    {
        var tenants = _provider.GetRequiredService<ITenantManager>();
        var manager = _provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var store = _provider.GetRequiredService<ITenantConnectionConfigurationStore>();
        var shared = await tenants.CreateAsync("shared", null, isActive: false);
        var dedicated = await tenants.CreateAsync("dedicated", null, isActive: false);
        await manager.SetAsync(shared.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);
        await manager.SetAsync(
            dedicated.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/dedicated",
            "migration/dedicated",
            expectedVersion: null);

        var configurations = await store.GetListAsync();

        Assert.Equal(2, configurations.Count);
        Assert.Contains(configurations, item => item.TenantId == shared.Id);
        Assert.Contains(configurations, item => item.TenantId == dedicated.Id);
    }

    /// <summary>
    /// 已删除租户的连接配置不可达。
    /// </summary>
    /// <remarks>
    /// 这条锁住的缺陷是<b>非对称的删除边界</b>：Identity 本地解析器自己 join 了
    /// 「租户未删」，而 Resource 宿主拿路由走的是本存储经 HTTP 暴露的读路径。
    /// 本存储少这道 join 时，删掉一个租户之后 Identity 拒绝它、Resource 却继续
    /// 按它的 Secret 路由到它的库，窗口是 max(令牌有效期, 路由缓存 TTL)。
    /// 控制面上下文是普通 DbContext，没有软删除过滤器兜底；连接配置行也不实现
    /// ISoftDelete（级联只在硬删时触发），所以判据只能显式写。
    /// </remarks>
    [Fact]
    public async Task Deleted_tenant_has_no_reachable_connection_configuration()
    {
        var tenants = _provider.GetRequiredService<ITenantManager>();
        var manager = _provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var store = _provider.GetRequiredService<ITenantConnectionConfigurationStore>();

        var tenant = await tenants.CreateAsync("doomed", null, isActive: false);
        await manager.SetAsync(tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);
        Assert.NotNull(await store.FindAsync(tenant.Id));

        await tenants.DeleteAsync(tenant.Id);

        // 行仍在库里（软删只翻 TenantRecord 的标志），但不再可达
        Assert.Null(await store.FindAsync(tenant.Id));
        Assert.DoesNotContain(await store.GetListAsync(), item => item.TenantId == tenant.Id);
    }

    /// <summary>
    /// 独立库租户被删除后同样不可达——它是后果最重的一档：
    /// 读到的话拿到的是指向该租户专属库的运行时 Secret 引用。
    /// </summary>
    [Fact]
    public async Task Deleted_dedicated_tenant_leaks_no_secret_reference()
    {
        var tenants = _provider.GetRequiredService<ITenantManager>();
        var manager = _provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var store = _provider.GetRequiredService<ITenantConnectionConfigurationStore>();

        var tenant = await tenants.CreateAsync("doomed-dedicated", null, isActive: false);
        await manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime-secret",
            "migration-secret",
            expectedVersion: null);

        await tenants.DeleteAsync(tenant.Id);

        Assert.Null(await store.FindAsync(tenant.Id));
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
