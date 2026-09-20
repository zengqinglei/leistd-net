using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;
using Leistd.Data.Connections;
using Leistd.TestBase.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.MultiTenancy.Tests.ConnectionResolution;

public class RegistrationAndOptionsTests
{
    [Fact]
    public void Remote_resolution_registers_a_scoped_resolver_and_a_host_singleton_coordinator()
    {
        var services = new ServiceCollection().AddRemoteTenantConnectionResolution();

        services.AssertSingle<IConnectionStringResolver>(ServiceLifetime.Scoped);
        services.AssertImplementedBy<IConnectionStringResolver, RemoteConnectionStringResolver>();
        services.AssertSingle<TenantRouteResolutionCoordinator>(ServiceLifetime.Singleton);
        services.AssertImplementedBy<ITenantMigrationTargetProvider, TenantMigrationTargetProvider>();
    }

    [Fact]
    public void Registering_remote_resolution_twice_is_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(services => services.AddRemoteTenantConnectionResolution());
    }

    [Fact]
    public void A_resolver_registered_by_the_host_is_kept()
    {
        var services = new ServiceCollection();
        services.AddScoped<IConnectionStringResolver, HostResolver>();

        services.AddRemoteTenantConnectionResolution();

        services.AssertImplementedBy<IConnectionStringResolver, HostResolver>();
    }

    [Fact]
    public void Local_resolution_registers_the_control_context_variant()
    {
        var services = new ServiceCollection()
            .AddLocalTenantConnectionResolution<ControlDbContext>(o => o.ControlPlaneConnectionStringName = "Control");

        services.AssertSingle<IConnectionStringResolver>(ServiceLifetime.Scoped);
        services.AssertImplementedBy<IConnectionStringResolver, LocalConnectionStringResolver<ControlDbContext>>();
        services.AssertImplementedBy<ITenantMigrationTargetProvider, TenantMigrationTargetProvider>();
    }

    // 远端宿主不持有控制面的密钥环：远端解析的注册面里不能冒出任何 Data Protection 依赖
    [Fact]
    public void Remote_resolution_does_not_require_a_data_protection_key_ring()
    {
        using var host = new RemoteHost();

        Assert.NotNull(host.Provider.GetRequiredService<IServiceScopeFactory>().CreateScope()
            .ServiceProvider.GetRequiredService<IConnectionStringResolver>());
    }

    // TTL 必须显式写在配置里：藏在代码默认值里，执行排空流程的人就无从知道该等多久
    [Theory]
    [InlineData(null)]
    [InlineData("00:00:00")]
    [InlineData("01:00:01")]
    public void An_unset_or_unusable_cache_lifetime_fails_validation(string? lifetime)
    {
        using var host = new RemoteHost(cacheLifetime: lifetime);

        Assert.Throws<OptionsValidationException>(
            () => host.Provider.GetRequiredService<IOptions<TenantRouteCacheOptions>>().Value);
    }

    [Fact]
    public void The_cache_lifetime_binds_from_the_TenantRouting_section()
    {
        using var host = new RemoteHost(cacheLifetime: "00:02:30");

        Assert.Equal(TimeSpan.FromSeconds(150),
            host.Provider.GetRequiredService<IOptions<TenantRouteCacheOptions>>().Value.CacheLifetime);
    }

    // 控制库必须钉在自己的连接名上，否则它会被当成租户数据按租户路由
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Default")]
    public void The_control_plane_name_is_required_and_not_Default(string? name)
    {
        using var provider = new ServiceCollection()
            .AddLocalTenantConnectionResolution<ControlDbContext>(o => o.ControlPlaneConnectionStringName = name)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LocalTenantConnectionOptions>>().Value);
    }

    private sealed class HostResolver : IConnectionStringResolver
    {
        public Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
            => Task.FromResult("host");
    }

    private sealed class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options);
}
