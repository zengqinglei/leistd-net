using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork.Core;
using Leistd.UnitOfWork.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

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
        await manager.SetAsync(target.Id, TenantDatabaseMode.SharedDatabase, null, null);

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
        await manager.SetAsync(shared.Id, TenantDatabaseMode.SharedDatabase, null, null);
        await manager.SetAsync(
            dedicated.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/dedicated",
            "migration/dedicated");

        var configurations = await store.GetListAsync();

        Assert.Equal(2, configurations.Count);
        Assert.Contains(configurations, item => item.TenantId == shared.Id);
        Assert.Contains(configurations, item => item.TenantId == dedicated.Id);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
