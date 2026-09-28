using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests;

/// <summary>
/// 注册可重复：DDD 基座替宿主调用一次，宿主再调用时不得出现两个总线实例；宿主预先替换本地总线时，
/// <see cref="IEventBus"/> 也指向它。
/// </summary>
public class LocalEventBusRegistrationTests
{
    [Fact]
    public void Registering_twice_keeps_a_single_bus()
    {
        var services = new ServiceCollection();
        services.AddLocalEventBus();
        services.AddLocalEventBus();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILocalEventBus));
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ILocalEventBus>(), provider.GetRequiredService<ILocalEventDispatcher>());
    }

    // 替换只注册 ILocalEventBus（与 dispatcher）即可：IEventBus 跟随它，不会留下一条走默认总线的发布路径
    [Fact]
    public void A_replaced_local_bus_also_serves_IEventBus()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalEventBus, HostBus>();
        services.AddLocalEventBus();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<HostBus>(provider.GetRequiredService<IEventBus>());
    }

    private sealed class HostBus : ILocalEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : Leistd.EventBus.Events.IEvent => Task.CompletedTask;

        public Task PublishAsync(Leistd.EventBus.Events.IEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
