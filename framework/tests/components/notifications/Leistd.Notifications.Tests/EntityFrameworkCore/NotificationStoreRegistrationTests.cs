using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Notifications.Abstractions;
using Leistd.Notifications.EntityFrameworkCore;
using Leistd.Notifications.EntityFrameworkCore.Stores;
using Xunit;

namespace Leistd.Notifications.Tests.EntityFrameworkCore;

public class NotificationStoreRegistrationTests
{
    // 两个上下文各注册一次时 Microsoft DI 静默取最后一条：通知落进宿主没预期的那个库。
    [Fact]
    public void Registering_the_store_for_a_second_context_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddNotificationsEfCore<FirstDbContext>();

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddNotificationsEfCore<SecondDbContext>());

        Assert.Contains("already registered", error.Message);
    }

    // 重复登记同一个上下文无害，按幂等处理。
    // 宿主提前把同一个实现登记成错误的生命周期：必须拒绝。放行的话紧随其后的 TryAdd 会因
    // "已有注册"不再补正确那条，框架最终保留宿主那个错的——EF 存储被登记成单例尤其糟，
    // 它会捕获作用域内的上下文。
    [Fact]
    public void A_host_registration_with_the_wrong_lifetime_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationStore, EfCoreNotificationStore<FirstDbContext>>();

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddNotificationsEfCore<FirstDbContext>());

        Assert.Contains("Singleton", error.Message);
    }

    [Fact]
    public void Registering_the_same_context_twice_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddNotificationsEfCore<FirstDbContext>();
        services.AddNotificationsEfCore<FirstDbContext>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(INotificationStore));
        Assert.Equal(typeof(EfCoreNotificationStore<FirstDbContext>), descriptor.ImplementationType);
        // 生命周期一并钉住：四家的保障要对称，否则误改一处不会红
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    private sealed class FirstDbContext(DbContextOptions<FirstDbContext> options) : DbContext(options);

    private sealed class SecondDbContext(DbContextOptions<SecondDbContext> options) : DbContext(options);
}
