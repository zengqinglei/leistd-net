using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork.Core;
using Leistd.UnitOfWork.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

public class TenantConnectionConfigurationManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private TestDbContext _dbContext = default!;
    private ITenantManager _tenantManager = default!;
    private ITenantConnectionConfigurationManager _manager = default!;
    private ITenantConnectionConfigurationStore _store = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddMultiTenancyEfCore<TestDbContext>();

        var provider = services.BuildServiceProvider();
        _dbContext = provider.GetRequiredService<TestDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();
        _tenantManager = provider.GetRequiredService<ITenantManager>();
        _manager = provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        _store = provider.GetRequiredService<ITenantConnectionConfigurationStore>();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Shared_database_rejects_secret_references()
    {
        var tenant = await CreateTenantAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.SharedDatabase,
            "runtime/tenant",
            null));
    }

    [Theory]
    [InlineData(null, "migration/tenant")]
    [InlineData("runtime/tenant", null)]
    [InlineData("", "migration/tenant")]
    [InlineData("runtime/tenant", "  ")]
    public async Task Dedicated_database_requires_both_secret_references(
        string? runtimeSecretReference,
        string? migrationSecretReference)
    {
        var tenant = await CreateTenantAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            runtimeSecretReference,
            migrationSecretReference));
    }

    [Fact]
    public async Task Set_creates_one_record_and_updates_increment_the_version()
    {
        var tenant = await CreateTenantAsync();

        var created = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/tenant-v1",
            "migration/tenant-v1");
        var updated = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/tenant-v2",
            "migration/tenant-v2");

        Assert.Equal(1, created.Version);
        Assert.Equal(2, updated.Version);
        Assert.Single(await _dbContext.Set<TenantConnectionRecord>().ToListAsync());

        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal("runtime/tenant-v2", stored.RuntimeSecretReference);
        Assert.Equal("migration/tenant-v2", stored.MigrationSecretReference);
    }

    [Fact]
    public async Task Missing_tenant_is_rejected()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(() => _manager.SetAsync(
            Guid.NewGuid(),
            TenantDatabaseMode.SharedDatabase,
            null,
            null));
    }

    [Fact]
    public async Task Shared_database_is_an_explicit_record_not_a_missing_configuration()
    {
        var tenant = await CreateTenantAsync();

        Assert.Null(await _store.FindAsync(tenant.Id));

        var configuration = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.SharedDatabase,
            null,
            null);

        Assert.Equal(TenantDatabaseMode.SharedDatabase, configuration.DatabaseMode);
        Assert.Null(configuration.RuntimeSecretReference);
        Assert.Null(configuration.MigrationSecretReference);
    }

    [Fact]
    public async Task Configuration_string_representation_does_not_expose_secret_references()
    {
        var tenant = await CreateTenantAsync();
        var configuration = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/highly-sensitive-reference",
            "migration/highly-sensitive-reference");

        var text = configuration.ToString();

        Assert.DoesNotContain("highly-sensitive", text, StringComparison.Ordinal);
    }

    private Task<TenantConfiguration> CreateTenantAsync() =>
        _tenantManager.CreateAsync($"tenant-{Guid.NewGuid():N}", null, isActive: false);

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
