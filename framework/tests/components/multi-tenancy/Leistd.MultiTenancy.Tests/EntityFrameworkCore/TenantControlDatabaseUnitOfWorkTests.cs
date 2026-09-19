using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.Stores;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.EntityFrameworkCore;

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
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
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
    public async Task Tenant_and_connection_registration_roll_back_together()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = await manager.BeginAsync();
            var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager>();
            var connectionManager = scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

            var tenant = await tenantManager.CreateAsync("atomic-tenant", null, isActive: false);
            await connectionManager.SetAsync(tenant.Id, "crm", "Host=acme;Database=acme", expectedVersion: null);

            await unitOfWork.RollbackAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await dbContext.Set<TenantRecord>().ToListAsync());
        Assert.Empty(await dbContext.Set<TenantConnectionRecord>().ToListAsync());
    }

    /// <summary>
    /// 建租户 + 登记连接在同一个原子边界内，且租户注册表与连接行始终同库同事务。
    /// </summary>
    /// <remarks>
    /// <para>租户创建这一步永远只碰控制库，"分库"影响的是该租户日后的业务数据落在哪儿，
    /// 而不是它的注册记录落在哪儿。这条不变量若被破坏（比如有人把连接登记搬去租户库），
    /// 建租户就会跨两个物理目标、被工作单元的连接绑定拦下——这个测试是那条设计的回归锁。</para>
    /// <para>不分库的租户在新模型里<b>没有连接行</b>，所以"共享"那一档不再是一条特殊记录，
    /// 而是下面 <c>A_tenant_without_registrations_commits_with_no_connection_rows</c> 的形态。</para>
    /// </remarks>
    [Theory]
    [InlineData("default")]
    [InlineData("crm")]
    public async Task Tenant_and_connection_registration_commit_together(string name)
    {
        const string ConnectionString = "Host=acme;Database=acme";
        Guid tenantId;

        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = await manager.BeginAsync();
            var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager>();
            var connectionManager = scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

            var tenant = await tenantManager.CreateAsync($"tenant-{name}", null, isActive: false);
            tenantId = tenant.Id;

            await connectionManager.SetAsync(tenant.Id, name, ConnectionString, expectedVersion: null);

            await unitOfWork.CompleteAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();

        Assert.Single(await dbContext.Set<TenantRecord>().Where(x => x.Id == tenantId).ToListAsync());

        var record = await dbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal(name, record.Name);
        Assert.NotEmpty(record.ProtectedConnectionString);

        var lookup = await verificationScope.ServiceProvider
            .GetRequiredService<ITenantConnectionConfigurationStore>().FindAsync(tenantId, name);
        Assert.Equal(ConnectionString, lookup!.Connection!.ConnectionString);
    }

    // 不分库的租户：建完就是没有任何连接行，解析因此回落到服务自己的配置
    [Fact]
    public async Task A_tenant_without_registrations_commits_with_no_connection_rows()
    {
        Guid tenantId;

        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = await manager.BeginAsync();
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantManager>()
                .CreateAsync("plain-tenant", null, isActive: false);
            tenantId = tenant.Id;
            await unitOfWork.CompleteAsync();
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await dbContext.Set<TenantConnectionRecord>().Where(x => x.TenantId == tenantId).ToListAsync());

        var lookup = await verificationScope.ServiceProvider
            .GetRequiredService<ITenantConnectionConfigurationStore>().FindAsync(tenantId, "crm");
        Assert.NotNull(lookup);
        Assert.False(lookup.HasAnyConnection);
        Assert.Null(lookup.Connection);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
