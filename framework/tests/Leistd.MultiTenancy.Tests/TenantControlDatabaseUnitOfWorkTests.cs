using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.Stores;

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
                migrationSecretReference: null,
                expectedVersion: null);

            await unitOfWork.RollbackAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await dbContext.Set<TenantRecord>().ToListAsync());
        Assert.Empty(await dbContext.Set<TenantConnectionRecord>().ToListAsync());
    }

    /// <summary>
    /// 建租户 + 写连接配置在两种数据库模式下都必须成立，且都在同一个原子边界内。
    /// </summary>
    /// <remarks>
    /// <para>这两条路径在代码上是同一段，但运行时形态不同，所以必须各测一次：
    /// 共享模式下连接配置与租户注册表同库同事务；独立模式下写入的是 Secret 引用
    /// （不是连接串本身），配置行仍然落在控制库里，所以同样是一个事务。</para>
    /// <para>换句话说：租户创建这一步永远只碰控制库，"独立库"影响的是该租户日后
    /// 的业务数据落在哪儿，而不是它的注册记录落在哪儿。这条不变量若被破坏
    /// （比如有人把连接配置搬去租户库），建租户就会跨两个物理目标、
    /// 被工作单元的连接绑定拦下——这个测试是那条设计的回归锁。</para>
    /// </remarks>
    [Theory]
    [InlineData(TenantDatabaseMode.SharedDatabase, null, null)]
    [InlineData(TenantDatabaseMode.DedicatedDatabase, "runtime/acme", "migration/acme")]
    public async Task Tenant_and_connection_configuration_commit_together_in_both_modes(
        TenantDatabaseMode mode,
        string? runtimeSecret,
        string? migrationSecret)
    {
        Guid tenantId;

        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = await manager.BeginAsync();
            var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager>();
            var connectionManager = scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

            var tenant = await tenantManager.CreateAsync($"tenant-{mode}", null, isActive: false);
            tenantId = tenant.Id;

            await connectionManager.SetAsync(tenant.Id, mode, runtimeSecret, migrationSecret, expectedVersion: null);

            await unitOfWork.CompleteAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();

        Assert.Single(await dbContext.Set<TenantRecord>().Where(x => x.Id == tenantId).ToListAsync());

        var configuration = await dbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal(mode, configuration.DatabaseMode);
        Assert.Equal(runtimeSecret, configuration.RuntimeSecretReference);
        Assert.Equal(migrationSecret, configuration.MigrationSecretReference);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
