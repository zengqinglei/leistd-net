using System.Security.Claims;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Claims;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.EntityFrameworkCore;

/// <summary>管理审计属于当前调用方，目标批次与外层事务分别完成。</summary>
public sealed class DistributedEventAuditContextTests
{
    /// <summary>外层提交或回滚不改变已提交批次的审计数量、身份与目标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_committed_target_batch_is_audited_as_the_caller_independently_of_the_outer_unit(bool commit)
    {
        await using var host = await DistributedEventTestHost.CreateAsync(configureServices: services =>
            services.AddSingleton<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>());
        await host.InTenantAsync(DistributedEventTestHost.DedicatedTenant, (_, db) =>
        {
            db.Add(new DistributedEventOutbox
            {
                EventId = Guid.NewGuid(), EventName = "order.paid", Payload = "{}", TenantId = DistributedEventTestHost.DedicatedTenant,
                Status = DistributedEventOutboxStatus.DeadLettered, OccurredAt = host.Clock.GetUtcNow().UtcDateTime,
                CreationTime = host.Clock.GetUtcNow().UtcDateTime, LastModificationTime = host.Clock.GetUtcNow().UtcDateTime
            });
            return Task.CompletedTask;
        });
        var row = await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().SingleAsync(), dedicated: true);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var identity = services.GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "host-operator")], "test")));
        using var tenant = services.GetRequiredService<ICurrentTenant>().Change(DistributedEventTestHost.DedicatedTenant);
        var units = services.GetRequiredService<IUnitOfWorkManager>();
        using (var outer = units.Begin(requiresNew: true))
        {
            var result = await services.GetRequiredService<IDistributedEventManagementService>().RedeliverAsync(new() { Ids = [row.Id] });
            Assert.Equal(1, result.AffectedCount);
            var audit = Assert.Single(host.ManagedEvents);
            Assert.Equal(DistributedEventTestHost.DedicatedTenant, Assert.Single(host.ManagedEventTenants));
            Assert.Equal("host-operator", Assert.Single(host.ManagedEventUsers));
            Assert.Equal(DistributedEventTestHost.DedicatedTenant, audit.TargetTenantId);
            Assert.Same(outer, units.Current);
            Assert.Equal(DistributedEventTestHost.DedicatedTenant, services.GetRequiredService<ICurrentTenant>().Id);
            if (commit) await outer.CompleteAsync();
            else await outer.RollbackAsync();
        }
        Assert.Single(host.ManagedEvents);
        Assert.Equal(DistributedEventOutboxStatus.Pending,
            (await host.ReadAsync(db => db.Set<DistributedEventOutbox>().IgnoreQueryFilters().SingleAsync(), dedicated: true)).Status);
    }
}
