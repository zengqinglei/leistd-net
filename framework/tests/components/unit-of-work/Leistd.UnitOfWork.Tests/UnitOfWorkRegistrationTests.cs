using Leistd.EventBus.Abstractions;
using Leistd.TestBase.Assertions;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.UnitOfWork.Tests;

/// <summary>
/// <c>AddUnitOfWork</c> / <c>AddUnitOfWorkEfCore</c> 的注册面：生命周期、幂等与宿主替换。
/// </summary>
/// <remarks>
/// 环境工作单元与管理器是单例（状态在 AsyncLocal 里）；工作单元本身是 Transient，每次开始都是新的。
/// 管理器被改成 Scoped 时，后台作业在根作用域里拿不到它；工作单元被改成单例时，两个请求会共用一个事务。
/// </remarks>
public sealed class UnitOfWorkRegistrationTests
{
    [Fact]
    public void Core_registration_uses_the_documented_lifetimes()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWork();

        services.AssertSingle<IAmbientUnitOfWork>(ServiceLifetime.Singleton);
        services.AssertSingle<IUnitOfWorkManager>(ServiceLifetime.Singleton);
        services.AssertSingle<IUnitOfWork>(ServiceLifetime.Transient);
        services.AssertSingle<UnitOfWorkInterceptor>(ServiceLifetime.Transient);
        services.AssertSingle<UnitOfWorkEventHandlerInterceptor>(ServiceLifetime.Transient);
        services.AssertSingle<ILocalEventDeferrer>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Core_registration_is_idempotent()
        => ServiceCollectionAssertions.AssertIdempotent(services => services.AddUnitOfWork());

    // 管理器是替换口：宿主先注册的实现不被组件默认值盖掉
    [Fact]
    public void Host_registered_unit_of_work_manager_is_kept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkManager>(_ => throw new NotSupportedException());

        services.AddUnitOfWork();

        Assert.NotNull(services.AssertSingle<IUnitOfWorkManager>(ServiceLifetime.Singleton).ImplementationFactory);
    }

    // 连接绑定是 Scoped：同一请求内的多个工作单元共用一个绑定，才能拒绝中途改道到另一个库
    [Fact]
    public void Ef_core_registration_uses_the_documented_lifetimes()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWorkEfCore();

        var provider = Assert.Single(services, d => d.ServiceType == typeof(IDbContextProvider<>));
        Assert.Equal((typeof(DbContextProvider<>), ServiceLifetime.Transient), (provider.ImplementationType, provider.Lifetime));
        // 两者是内部类型，按名字定位描述符
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType.Name == "UnitOfWorkConnectionBinding").Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType.Name == "DbContextConfiguredTargetCache").Lifetime);
    }

    [Fact]
    public void Ef_core_registration_is_idempotent()
        => ServiceCollectionAssertions.AssertIdempotent(services => services.AddUnitOfWorkEfCore());

    // 宿主为某个上下文提供专用的上下文来源时，封闭泛型注册优先于组件的开放泛型默认值，二者并存
    [Fact]
    public void Host_closed_db_context_provider_coexists_with_the_open_generic_default()
    {
        var services = new ServiceCollection();
        services.AddTransient<IDbContextProvider<HostDbContext>>(_ => throw new NotSupportedException());

        services.AddUnitOfWorkEfCore();

        Assert.NotNull(services.AssertSingle<IDbContextProvider<HostDbContext>>(ServiceLifetime.Transient).ImplementationFactory);
        Assert.Single(services, d => d.ServiceType == typeof(IDbContextProvider<>));
    }

    private sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options);
}
