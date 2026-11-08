using Leistd.EventBus.Abstractions;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Events;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>发布去重限于当前事务实际发布的冻结事实，不代替跨事务的唯一约束。</summary>
public sealed class DistributedEventPublishingTests
{
    /// <summary>冲刷不结束工作单元；相同事实只持久化、入队并发送一次。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Repeated_publication_in_one_unit_is_idempotent_after_optional_save(bool save, bool tenant)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var message = new PaidEvent { OrderId = 7 };
        await host.InTenantAsync(tenant ? DistributedEventTestHost.TenantA : null, async (services, _) =>
        {
            await services.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
            var unit = services.GetRequiredService<IUnitOfWorkManager>().Current!;
            if (save) await unit.SaveChangesAsync();
            await unit.ServiceProvider.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
        });

        Assert.Equal(1, host.Queue.Count);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().CountAsync()));
        await host.Queue.RunAllAsync(host.Services);
        Assert.Single(host.Transport.Sent);
    }

    /// <summary>嵌套参与边界与父级共享事务，不能再登记一次同一事实。</summary>
    [Fact]
    public async Task Participating_child_units_share_the_publication_boundary()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var message = new PaidEvent { OrderId = 7 };
        await host.InTenantAsync(null, async (services, _) =>
        {
            await services.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
            using var child = services.GetRequiredService<IUnitOfWorkManager>().Begin();
            await child.ServiceProvider.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
            await child.CompleteAsync();
        });

        Assert.Equal(1, host.Queue.Count);
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync()));
    }

    /// <summary>复用标识但改变名称或冻结正文时，异常从发布点传播并回滚业务写入。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_facts_fail_at_publication_and_rollback_business_writes(bool changeName)
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var message = new PaidEvent { OrderId = 7 };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InTenantAsync(null, async (services, db) =>
        {
            db.Add(new EventProjection { EventId = message.EventId, Count = 1 });
            var bus = services.GetRequiredService<IDistributedEventBus>();
            await bus.PublishAsync(message);
            if (changeName)
                await bus.PublishAsync(new ChangedPaidEvent { EventId = message.EventId, OccurredOn = message.OccurredOn, OrderId = 7 });
            else
            {
                message.OrderId = 8;
                await bus.PublishAsync(message);
            }
        }));

        Assert.Contains(message.EventId.ToString(), failure.Message);
        Assert.Contains(changeName ? "order.updated" : "order.paid", failure.Message);
        Assert.Equal(0, await host.ReadAsync(db => db.Set<EventProjection>().CountAsync()));
        Assert.Equal(0, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync()));
        Assert.Equal(0, host.Queue.Count);
    }

    /// <summary>预先加载旧事务的行不等于在新事务发布过，数据库唯一约束仍拒绝重复。</summary>
    [Fact]
    public async Task A_new_unit_cannot_deduplicate_against_a_tracked_row_from_an_earlier_transaction()
    {
        await using var host = await DistributedEventTestHost.CreateAsync();
        var message = new PaidEvent { OrderId = 7 };
        await host.InTenantAsync(null, (services, _) => services.GetRequiredService<IDistributedEventBus>().PublishAsync(message));
        host.Queue.Drop();

        await Assert.ThrowsAsync<DbUpdateException>(() => host.InTenantAsync(null, async (services, db) =>
        {
            await db.Set<DistributedEventOutbox>().SingleAsync();
            await services.GetRequiredService<IDistributedEventBus>().PublishAsync(message);
        }));
        Assert.Equal(1, await host.ReadAsync(db => db.Set<DistributedEventOutbox>().CountAsync()));
        Assert.Equal(0, host.Queue.Count);
    }

    [EventName("order.updated")]
    private sealed class ChangedPaidEvent : DistributedEvent
    {
        public int OrderId { get; set; }
    }
}
