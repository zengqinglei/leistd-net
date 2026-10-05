using Leistd.Settings.Validation;
using CompanyName.ProjectName.Api;
using CompanyName.ProjectName.Api.Auth;
#if (IncludeNotifications && LocalIdentity)
using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Api.Notifications;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
#endif
using CompanyName.ProjectName.Application;
#if (IncludeRealTime)
using CompanyName.ProjectName.Application.RealTime;
#endif
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Domain.Users.DomainServices;
#if (LocalIdentity && IncludeMultiTenancy)
using CompanyName.ProjectName.Infrastructure;
using CompanyName.ProjectName.Infrastructure.TenantConnections;
using Leistd.MultiTenancy.Management.Provisioning;
#endif
using Leistd.ObjectMapping.Abstractions;
using Leistd.ObjectMapping.Mapster.Options;
#if (IncludeRealTime)
using Leistd.RealTime.Subscriptions;
#endif
using Microsoft.AspNetCore.Authorization;
#if (LocalIdentity && IncludeMultiTenancy)
using Microsoft.Extensions.Configuration;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Registration;

/// <summary>
/// 注册面：各层入口 <c>AddDomainServices()</c>、<c>AddApplicationServices()</c>、<c>AddApiAuthorization()</c>，
/// 以及与组件入口的组合顺序。
/// </summary>
/// <remarks>
/// 注册结果是组合根的契约，编译期完全看不出来：生命周期写错、重复注册、
/// 后注册意外覆盖先注册，全都要到运行时才发作。
/// 在 <see cref="IServiceCollection"/> 上断言不需要宿主，成本是集成测试的百分之一。
/// </remarks>
public class ServiceRegistrationTests
{
    [Fact]
    public void Domain_services_are_registered_as_transient()
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

    // 设置页在所有服务形态下都保留：值域之外的业务校验（时区等）必须在每种形态下都登记，
    // 漏了不会报错，只会让非法值静默落库，之后每个消费方都得自己防御。
    [Fact]
    public void Setting_value_validators_are_registered_in_every_service_role()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplicationServices();

        Assert.Contains(services, d => d.ServiceType == typeof(ISettingValueValidator));
    }

    [Fact]
    public void Application_registration_brings_in_the_object_mapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplicationServices();

        Assert.Contains(services, d => d.ServiceType == typeof(IObjectMapper));
    }

    // 映射配置经程序集扫描发现，扫描在构建映射器时执行：不需要宿主也能解析出可用的映射器。
    [Fact]
    public void Application_services_resolve_without_a_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IObjectMapper>());
    }

    // 重复调用不能让多实现扩展点（事件处理器、设置校验器、定义提供方）多出一份——
    // 那会让每个事件处理两次、每条定义登记两次；映射扫描也只登记一次。
    // 组件入口内部的 Options 配置回调可能随调用累加，因此按实际效果比较，而不是比较描述符总数。
    [Fact]
    public void Application_registration_is_idempotent()
    {
        var once = new ServiceCollection().AddLogging().AddApplicationServices();
        var twice = new ServiceCollection().AddLogging().AddApplicationServices().AddApplicationServices();

        Assert.Equal(Registrations(once), Registrations(twice));
        using var onceProvider = once.BuildServiceProvider();
        using var twiceProvider = twice.BuildServiceProvider();
        Assert.Equal(
            onceProvider.GetRequiredService<IOptions<MapsterOptions>>().Value.Configurators.Count,
            twiceProvider.GetRequiredService<IOptions<MapsterOptions>>().Value.Configurators.Count);
    }
#if (IncludeRealTime)

    // 实时 Hub 映射时要求订阅授权器已登记（框架不给默认实现）：它属于应用层的授权规则，随应用层入口登记
    [Fact]
    public void Application_registration_chooses_the_realtime_subscription_authorizer()
    {
        var services = new ServiceCollection().AddLogging().AddApplicationServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRealTimeSubscriptionAuthorizer));
        Assert.Equal(typeof(AppRealTimeSubscriptionAuthorizer), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }
#endif
#if (LocalIdentity && IncludeMultiTenancy)

    // 组件（AddTenantManagement）按 TryAdd 挂"不翻译"的默认实现，本项目有意覆盖：
    // 不论哪一层先注册，生效的都是 PostgreSQL 方言的翻译，且只有一条
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_postgres_tenant_error_describer_wins_in_either_registration_order(bool infrastructureFirst)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();

        if (infrastructureFirst)
        {
            services.AddInfrastructureServices(configuration);
            services.AddApplicationServices();
        }
        else
        {
            services.AddApplicationServices();
            services.AddInfrastructureServices(configuration);
        }

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ITenantDatabaseErrorDescriber));
        Assert.Equal(typeof(PostgresTenantDatabaseErrorDescriber), descriptor.ImplementationType);
    }
#endif
#if (IncludeNotifications && LocalIdentity)

    // 应用层默认不发安全提醒（TryAdd），启用通知时宿主有意替换为经通知组件发布：与调用先后无关
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Notification_security_alerts_replace_the_silent_default_in_either_order(bool applicationFirst)
    {
        var services = new ServiceCollection().AddLogging();

        if (applicationFirst)
        {
            services.AddApplicationServices();
            services.AddMyProjectNotifications();
        }
        else
        {
            services.AddMyProjectNotifications();
            services.AddApplicationServices();
        }

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISecurityAlertPublisher));
        Assert.Equal(typeof(NotificationSecurityAlertPublisher), descriptor.ImplementationType);
    }
#endif

    // ASP.NET Core 只认一个授权结果处理器：本项目的处理器替换官方默认实现，与 AddAuthorization 的先后无关，重复调用也只有一条
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Api_authorization_replaces_the_default_result_handler_in_either_order(bool apiFirst)
    {
        var services = new ServiceCollection();

        if (apiFirst)
        {
            services.AddApiAuthorization();
            services.AddAuthorization();
        }
        else
        {
            services.AddAuthorization();
            services.AddApiAuthorization();
        }
        services.AddApiAuthorization();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
        Assert.Equal(typeof(ApiAuthorizationResultHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    // 服务类型、实现与生命周期的多重集合；Options 配置回调按实际效果另行比较
    private static List<string> Registrations(IServiceCollection services) =>
        services
            .Where(d => !IsOptionsCallback(d.ServiceType))
            .Select(d => $"{d.ServiceType.FullName}|{d.Lifetime}|" +
                (d.ImplementationType?.FullName ?? d.ImplementationInstance?.GetType().FullName ?? "factory"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool IsOptionsCallback(Type serviceType) =>
        serviceType.IsGenericType &&
        serviceType.GetGenericTypeDefinition() is var definition &&
        (definition == typeof(IConfigureOptions<>) || definition == typeof(IPostConfigureOptions<>));
}
