using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork.Core;
using Leistd.UnitOfWork.Core.Uow;
using Leistd.UnitOfWork.EfCore;
using Leistd.UnitOfWork.EfCore.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

public sealed class TenantControlDatabaseUnitOfWorkTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddDbContext<TestDbContext>((_, options) => options.UseSqlite(_connection));
        services.AddMultiTenancyEfCore<TestDbContext>();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Tenant_and_connection_configuration_roll_back_together()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = await manager.BeginAsync();
            var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager>();
            var connectionManager = scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

            var tenant = await tenantManager.CreateAsync("atomic-tenant", null, isActive: false);
            await connectionManager.SetAsync(
                tenant.Id,
                TenantDatabaseMode.SharedDatabase,
                runtimeSecretReference: null,
                migrationSecretReference: null);

            await unitOfWork.RollbackAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await dbContext.Set<TenantRecord>().ToListAsync());
        Assert.Empty(await dbContext.Set<TenantConnectionRecord>().ToListAsync());
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
