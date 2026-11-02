using Leistd.EventBus.Abstractions;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Local;
using Leistd.ExceptionHandling;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.Events;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>
/// 管理用例发布的订阅变更事件：宿主据此留痕，所以"发不发、发哪一种、何时分发"是契约。
/// 发哪一种用只记录的总线钉住；何时分发经真实事件总线与工作单元的推迟机制验证。
/// </summary>
public sealed class WebhookSubscriptionChangedEventTests
{
    /// <summary>创建、轮换、停用、启用、删除各发一条对应类别的事件，带订阅标识与名称快照；停用带手动原因。</summary>
    [Fact]
    public async Task Each_lifecycle_write_publishes_its_change_kind()
    {
        await using var host = await WebhookTestHost.CreateAsync();

        var id = (await CreateAsync(host)).Subscription.Id;
        await host.ManageAsync(null, service => service.RotateSecretAsync(id));
        await host.ManageAsync(null, service => service.DisableSubscriptionAsync(id));
        await host.ManageAsync(null, service => service.EnableSubscriptionAsync(id));
        await host.ManageAsync(null, service => service.DeleteSubscriptionAsync(id));

        var published = host.Events.Published.Cast<WebhookSubscriptionChangedEvent>().ToList();
        Assert.Equal(
            [
                WebhookSubscriptionChangeKind.Created,
                WebhookSubscriptionChangeKind.SecretRotated,
                WebhookSubscriptionChangeKind.Disabled,
                WebhookSubscriptionChangeKind.Enabled,
                WebhookSubscriptionChangeKind.Deleted
            ],
            published.Select(x => x.Change));
        Assert.All(published, x => Assert.Equal((id, "Audited hook"), (x.SubscriptionId, x.SubscriptionName)));
        Assert.Equal(
            [null, null, WebhookDisabledReason.Manual, null, null],
            published.Select(x => x.DisabledReason));
    }

    /// <summary>状态没变（再启用、再停用）与只改配置的更新不发布：审计里不该出现没发生过的启停。</summary>
    [Fact]
    public async Task Writes_that_change_no_lifecycle_state_publish_nothing()
    {
        await using var host = await WebhookTestHost.CreateAsync();
        var id = (await CreateAsync(host)).Subscription.Id;
        host.Events.Published.Clear();

        await host.ManageAsync(null, service => service.EnableSubscriptionAsync(id, retryFailed: true));
        await host.ManageAsync(null, service => service.UpdateSubscriptionAsync(id, new UpdateWebhookSubscriptionInputDto
        {
            Name = "Renamed",
            Url = "https://receiver.example/renamed",
            EventTypes = [WebhookTestHost.OrderShipped]
        }));
        await host.ManageAsync(null, service => service.DisableSubscriptionAsync(id));
        host.Events.Published.Clear();
        await host.ManageAsync(null, service => service.DisableSubscriptionAsync(id));

        Assert.Empty(host.Events.Published);
    }

    /// <summary>事件在提交之后才分发，处理器此时从另一个连接已经读得到这次写入。</summary>
    [Fact]
    public async Task The_event_is_dispatched_after_the_write_is_committed()
    {
        var probe = new Probe();
        await using var host = await CreateWithRealBusAsync(probe);
        probe.Exists = id => host.ReadAsync(db => db.Set<WebhookSubscription>().IgnoreQueryFilters().AnyAsync(x => x.Id == id));

        var id = (await CreateAsync(host)).Subscription.Id;
        await host.ManageAsync(null, service => service.DeleteSubscriptionAsync(id));

        Assert.Equal(
            [(WebhookSubscriptionChangeKind.Created, true), (WebhookSubscriptionChangeKind.Deleted, false)],
            probe.AfterCommit);
    }

    /// <summary>外层工作单元没有完成（回滚）时不分发：宿主不会为没落库的写入留下"成功"的记录。</summary>
    [Fact]
    public async Task Nothing_is_dispatched_when_the_outer_unit_of_work_does_not_complete()
    {
        var probe = new Probe();
        await using var host = await CreateWithRealBusAsync(probe);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            await scope.ServiceProvider.GetRequiredService<IWebhookManagementService>().CreateSubscriptionAsync(Input());
        }

        Assert.Empty(probe.Calls);
        // 对照：同样的调用在没有外层工作单元时确实会分发，上面的"空"不是探针失灵
        await CreateAsync(host);
        Assert.Single(probe.AfterCommit);
    }

    /// <summary>用例失败（订阅不存在）时不发布。</summary>
    [Fact]
    public async Task A_failed_write_publishes_nothing()
    {
        await using var host = await WebhookTestHost.CreateAsync();
        var missing = Guid.NewGuid();

        foreach (var write in new Func<IWebhookManagementService, Task>[]
                 {
                     service => service.RotateSecretAsync(missing),
                     service => service.EnableSubscriptionAsync(missing),
                     service => service.DisableSubscriptionAsync(missing),
                     service => service.DeleteSubscriptionAsync(missing)
                 })
        {
            await Assert.ThrowsAsync<BusinessException>(() => host.ManageAsync(null, write));
        }

        Assert.Empty(host.Events.Published);
    }

    private static Task<WebhookTestHost> CreateWithRealBusAsync(Probe probe)
        => WebhookTestHost.CreateAsync(services =>
        {
            services.RemoveAll<ILocalEventBus>();
            services.AddLocalEventBus();
            services.AddSingleton(probe);
            services.AddScoped<IEventHandler<WebhookSubscriptionChangedEvent>, ProbeHandler>();
        });

    private static Task<WebhookSubscriptionSecretOutputDto> CreateAsync(WebhookTestHost host)
        => host.ManageAsync(null, service => service.CreateSubscriptionAsync(Input()));

    private static CreateWebhookSubscriptionInputDto Input() => new()
    {
        Name = "Audited hook",
        Url = "https://receiver.example/hook",
        EventTypes = [WebhookTestHost.OrderPaid]
    };

    private sealed class Probe
    {
        public Func<Guid, Task<bool>> Exists { get; set; } = _ => Task.FromResult(false);

        public List<(UnitOfWorkPhase? Phase, WebhookSubscriptionChangeKind Change)> Calls { get; } = [];

        public List<(WebhookSubscriptionChangeKind Change, bool Visible)> AfterCommit { get; } = [];
    }

    // 测试宿主不织入工作单元的阶段拦截器，未标注的处理器在提交前后两趟都会被调到；阶段过滤是工作单元组件
    // 自己的契约（另有测试），这里只取提交后那一趟断言"已落库"
    private sealed class ProbeHandler(Probe probe) : IEventHandler<WebhookSubscriptionChangedEvent>
    {
        public async Task HandleAsync(WebhookSubscriptionChangedEvent @event, CancellationToken cancellationToken = default)
        {
            var phase = UnitOfWorkContext.CurrentPhase;
            probe.Calls.Add((phase, @event.Change));
            if (phase == UnitOfWorkPhase.AfterCommit)
            {
                probe.AfterCommit.Add((@event.Change, await probe.Exists(@event.SubscriptionId)));
            }
        }
    }
}
