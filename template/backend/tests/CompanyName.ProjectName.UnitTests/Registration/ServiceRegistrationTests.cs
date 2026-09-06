using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using Leistd.ObjectMapping.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.UnitTests.Registration;

/// <summary>
/// 注册面：<c>AddDomainServices()</c> 与 <c>AddApplicationServices()</c>。
/// </summary>
/// <remarks>
/// 注册结果是组合根的契约，编译期完全看不出来：生命周期写错、重复注册、
/// 后注册意外覆盖先注册，全都要到运行时才发作。
/// 在 <see cref="IServiceCollection"/> 上断言不需要宿主，成本是集成测试的百分之一。
/// </remarks>
public class ServiceRegistrationTests
{
    [Fact]
    public void Domain_services_are_registered_with_a_scoped_friendly_lifetime()
    {
        var services = new ServiceCollection();

        services.AddDomainServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(UserDomainService));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    // 组合根拆分后重复调用是常态。不幂等会让同一实现出现多条，
    // 按 IEnumerable 解析时重复执行，单例被建两份。
    [Fact]
    public void Domain_registration_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddDomainServices();
        var afterFirst = services.Count;
        services.AddDomainServices();

        Assert.Equal(afterFirst, services.Count);
    }

    [Fact]
    public void Application_registration_brings_in_the_object_mapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplicationServices();

        Assert.Contains(services, d => d.ServiceType == typeof(IObjectMapper));
    }

    // 应用层的映射配置经程序集扫描发现。扫描只应发生在这一次显式调用里——
    // 它一旦渗进更底层的注册面，每建一次容器都要遍历整个程序集的类型。
    [Fact]
    public void Application_services_resolve_without_a_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IObjectMapper>());
    }
}
