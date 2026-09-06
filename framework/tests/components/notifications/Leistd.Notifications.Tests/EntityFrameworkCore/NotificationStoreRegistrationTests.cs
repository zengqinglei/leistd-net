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
    [Fact]
    public void Registering_the_same_context_twice_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddNotificationsEfCore<FirstDbContext>();
        services.AddNotificationsEfCore<FirstDbContext>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(INotificationStore));
        Assert.Equal(typeof(EfCoreNotificationStore<FirstDbContext>), descriptor.ImplementationType);
    }

    private sealed class FirstDbContext(DbContextOptions<FirstDbContext> options) : DbContext(options);

    private sealed class SecondDbContext(DbContextOptions<SecondDbContext> options) : DbContext(options);
}
