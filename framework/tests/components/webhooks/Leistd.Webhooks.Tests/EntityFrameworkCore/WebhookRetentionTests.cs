using Leistd.BackgroundJobs.Recurring;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Options;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>保留期：按物理库逐个删除早于保留期的投递与尝试，再删已没有投递的旧事件；同库其他租户一并覆盖。</summary>
public sealed class WebhookRetentionTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// 63 天前的投递连同尝试删除；61 天前的保留；旧事件只有在没有投递引用时才删除。
    /// 共享库里的租户 A 与专属库都被覆盖。
    /// </summary>
    [Fact]
    public async Task Expired_deliveries_attempts_and_orphaned_events_are_deleted_in_every_database()
    {
        var oldEvent = await SeedAsync(WebhookTestHost.TenantA, ageDays: 63, deliveryAges: [63]);
        var sharedEvent = await SeedAsync(null, ageDays: 63, deliveryAges: [63, 61]);
        var freshEvent = await SeedAsync(null, ageDays: 10, deliveryAges: [10]);
        await SeedAsync(WebhookTestHost.DedicatedTenant, ageDays: 63, deliveryAges: [63]);

        await _host.RunRetentionAsync();

        var hostEvents = await _host.ReadAsync(db => db.Set<WebhookEvent>().IgnoreQueryFilters().Select(x => x.Id).ToListAsync());
        Assert.DoesNotContain(oldEvent, hostEvents);
        Assert.Contains(sharedEvent, hostEvents);
        Assert.Contains(freshEvent, hostEvents);
        Assert.All(await _host.DeliveriesAsync(), x => Assert.True(x.CreationTime > _host.Now.AddDays(-62)));
        Assert.Equal(2, (await _host.DeliveriesAsync()).Count);
        Assert.Equal(2, await _host.ReadAsync(db => db.Set<WebhookAttempt>().CountAsync()));

        Assert.Empty(await _host.DeliveriesAsync(_host.DedicatedConnectionString));
        Assert.Equal(0, await _host.ReadAsync(db => db.Set<WebhookEvent>().IgnoreQueryFilters().CountAsync(), _host.DedicatedConnectionString));
        Assert.Equal(0, await _host.ReadAsync(db => db.Set<WebhookAttempt>().CountAsync(), _host.DedicatedConnectionString));
    }

    /// <summary>集群任务；开关登记给调度器并按每轮作用域里的配置求值。</summary>
    [Fact]
    public void The_retention_job_is_a_cluster_job_with_a_switch_that_follows_the_setting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Leistd:Webhooks:Retention:Enabled"] = "false" })
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddWebhookRetention<WebhookTestDbContext>()
            .BuildServiceProvider();
        var definition = provider.GetServices<RecurringJobDefinition>().Single(d => d.Name == "webhooks.retention");

        Assert.Equal(RecurringJobScope.Cluster, definition.Scope);
        using (var off = provider.CreateScope())
            Assert.False(definition.IsEnabled!(off.ServiceProvider));

        configuration["Leistd:Webhooks:Retention:Enabled"] = "true";
        using var on = provider.CreateScope();
        Assert.True(definition.IsEnabled!(on.ServiceProvider));
    }

    /// <summary>执行时刻与批大小越界在启动期拒绝，消息以实际配置键开头。</summary>
    [Theory]
    [InlineData("DailyRunHourUtc", "24")]
    [InlineData("BatchSize", "99")]
    [InlineData("Days", "0")]
    public void Out_of_range_retention_options_are_rejected_with_the_configuration_key(string key, string value)
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Leistd:Webhooks:Retention:" + key] = value })
                .Build())
            .AddWebhookRetention<WebhookTestDbContext>()
            .BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WebhookRetentionOptions>>().Value);
        Assert.StartsWith("Leistd:Webhooks:Retention:" + key, Assert.Single(exception.Failures));
    }

    private Task<Guid> SeedAsync(Guid? tenantId, int ageDays, int[] deliveryAges)
        => _host.InTenantAsync(tenantId, async (_, db) =>
        {
            var subscription = await db.Set<WebhookSubscription>().FirstOrDefaultAsync();
            if (subscription is null)
            {
                subscription = new WebhookSubscription { TenantId = tenantId, Name = "s", Url = "https://r.example", Secret = "x", CreationTime = _host.Now };
                db.Add(subscription);
            }

            var webhookEvent = new WebhookEvent { TenantId = tenantId, Type = WebhookTestHost.OrderPaid, Payload = "{}", OccurredAt = _host.Now.AddDays(-ageDays) };
            db.Add(webhookEvent);
            foreach (var age in deliveryAges)
            {
                var delivery = new WebhookDelivery
                {
                    TenantId = tenantId,
                    EventId = webhookEvent.Id,
                    SubscriptionId = subscription.Id,
                    Status = WebhookDeliveryStatus.Succeeded,
                    CreationTime = _host.Now.AddDays(-age)
                };
                db.Add(delivery);
                db.Add(new WebhookAttempt { DeliveryId = delivery.Id, AttemptedAt = delivery.CreationTime, Trigger = WebhookAttemptTrigger.Automatic });
            }

            return webhookEvent.Id;
        });
}
