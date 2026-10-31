using Leistd.Data.Connections;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.EntityFrameworkCore;

/// <summary>本地连接路由不能预占调用方工作单元的控制库上下文。</summary>
public sealed class LocalResolutionTransactionTests
{
    /// <summary>业务先取连接，控制库仍能加入同一事务；回滚前冲刷两种写入，再在新作用域核验。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Business_then_control_share_a_transaction_with_local_resolution(bool tenantAvailable, bool complete)
    {
        var connectionString = $"Data Source=local-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString }).Build());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddLocalTenantConnectionResolution<ControlDbContext>(o => o.ControlPlaneConnectionStringName = "Control");
        services.AddDbContext<ControlDbContext>(o => Configure(o, connectionString));
        services.AddDbContext<BusinessDbContext>(o => Configure(o, connectionString));
        await using var provider = services.BuildServiceProvider();
        var tenantId = Guid.NewGuid();
        await using (var seed = provider.CreateAsyncScope())
        {
            var control = seed.ServiceProvider.GetRequiredService<ControlDbContext>();
            var business = seed.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.NotSame(control.Database.GetDbConnection(), business.Database.GetDbConnection());
            await control.Database.EnsureCreatedAsync();
            await business.Database.ExecuteSqlRawAsync("CREATE TABLE BusinessRows (Id TEXT PRIMARY KEY)");
            control.Add(new TenantRecord { Id = tenantId, Name = "route", NormalizedName = "ROUTE" });
            await control.SaveChangesAsync();
        }
        using var tenant = provider.GetRequiredService<ICurrentTenant>().Change(tenantAvailable ? tenantId : null);
        using (var uow = provider.GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var business = await provider.GetRequiredService<IDbContextProvider<BusinessDbContext>>().GetDbContextAsync();
            var control = await provider.GetRequiredService<IDbContextProvider<ControlDbContext>>().GetDbContextAsync();
            Assert.Same(business.Database.GetDbConnection(), control.Database.GetDbConnection());
            business.Add(new BusinessRow());
            control.Add(new TenantRecord { Name = "written", NormalizedName = "WRITTEN" });
            await uow.SaveChangesAsync();
            if (complete)
                await uow.CompleteAsync();
            else
                await uow.RollbackAsync();
        }
        await using var verification = provider.CreateAsyncScope();
        Assert.Equal(complete ? 1 : 0, await verification.ServiceProvider.GetRequiredService<BusinessDbContext>().Set<BusinessRow>().CountAsync());
        Assert.Equal(complete ? 2 : 1, await verification.ServiceProvider.GetRequiredService<ControlDbContext>().Set<TenantRecord>().CountAsync());
    }

    /// <summary>无租户和控制库名都不需要注册表上下文，连其工厂也不应调用。</summary>
    [Theory]
    [InlineData(false, "Default")]
    [InlineData(true, "Control")]
    public async Task Host_and_control_names_do_not_create_a_query_context(bool tenantAvailable, string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Data Source=host" }).Build());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddLocalTenantConnectionResolution<ControlDbContext>(o => o.ControlPlaneConnectionStringName = "Control");
        services.AddScoped<ControlDbContext>(_ => throw new InvalidOperationException("Query context must not be created."));
        await using var provider = services.BuildServiceProvider();
        using var tenant = provider.GetRequiredService<ICurrentTenant>().Change(tenantAvailable ? Guid.NewGuid() : null);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal("Data Source=host", await scope.ServiceProvider.GetRequiredService<IConnectionStringResolver>().ResolveAsync(name));
    }

    private static void Configure(DbContextOptionsBuilder options, string configured)
    {
        var creation = DbContextCreationContext.Current;
        if (creation?.ExistingConnection is { } connection)
            options.UseSqlite(connection);
        else
            options.UseSqlite(creation?.ConnectionString ?? configured);
    }

    [ConnectionStringName("Control")]
    private sealed class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureMultiTenancy();
    }

    private sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
    {
        public DbSet<BusinessRow> BusinessRows => Set<BusinessRow>();
    }

    private sealed class BusinessRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
