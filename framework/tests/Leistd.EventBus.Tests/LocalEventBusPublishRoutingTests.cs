using Leistd.EventBus.Core.Event;
using Leistd.EventBus.Core.EventBus;
using Leistd.EventBus.Core.EventHandler;
using Leistd.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests;

/// <summary>
/// 回归测试：本地事件按<b>运行时类型</b>路由到具体 handler。
/// </summary>
/// <remarks>
/// 防止重载陷阱回归：以基接口（ILocalEvent）的静态类型发布时，曾误选泛型重载
/// PublishAsync&lt;ILocalEvent&gt; → 解析 IEventHandler&lt;ILocalEvent&gt; → 找不到具体 handler → 静默丢事件。
/// </remarks>
public class LocalEventBusPublishRoutingTests
{
    private sealed class SampleEvent : LocalEvent
    {
    }

    private sealed class SampleEventHandler : IEventHandler<SampleEvent>
    {
        public static int Invoked;

        public Task HandleAsync(SampleEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Invoked);
            return Task.CompletedTask;
        }
    }

    private static IServiceProvider BuildProvider()
    {
        SampleEventHandler.Invoked = 0;
        return new ServiceCollection()
            .AddLocalEventBus()
            .AddScoped<IEventHandler<SampleEvent>, SampleEventHandler>()
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Publish_via_base_interface_static_type_invokes_concrete_handler()
    {
        var sp = BuildProvider();
        var bus = sp.GetRequiredService<ILocalEventBus>();

        // 关键：以基接口 ILocalEvent 的静态类型发布（模拟拦截器/UoW 的 List<ILocalEvent> 循环）
        ILocalEvent @event = new SampleEvent();
        await bus.PublishAsync(@event);

        Assert.Equal(1, SampleEventHandler.Invoked);
    }

    [Fact]
    public async Task Publish_via_concrete_static_type_invokes_concrete_handler()
    {
        var sp = BuildProvider();
        var bus = sp.GetRequiredService<ILocalEventBus>();

        var @event = new SampleEvent();
        await bus.PublishAsync(@event);

        Assert.Equal(1, SampleEventHandler.Invoked);
    }

    [Fact]
    public async Task Publish_via_IEvent_static_type_invokes_concrete_handler()
    {
        var sp = BuildProvider();
        var bus = sp.GetRequiredService<ILocalEventBus>();

        IEvent @event = new SampleEvent();
        await bus.PublishAsync(@event);

        Assert.Equal(1, SampleEventHandler.Invoked);
    }
}
