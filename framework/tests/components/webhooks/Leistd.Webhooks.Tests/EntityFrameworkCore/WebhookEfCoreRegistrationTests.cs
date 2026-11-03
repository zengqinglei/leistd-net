using Leistd.BackgroundJobs.Recurring;
using Leistd.TestBase.Assertions;
using Leistd.Webhooks.EntityFrameworkCore;
using Leistd.Webhooks.EntityFrameworkCore.Publishing;
using Leistd.Webhooks.EntityFrameworkCore.Stores;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Publishing;
using Leistd.Webhooks.Stores;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary><c>AddWebhooksEfCore</c> 的注册面：发布器、统计读取、兜底扫描任务，以及与 Core 入口的组合。</summary>
public sealed class WebhookEfCoreRegistrationTests
{
    /// <summary>发布器、统计读取与管理存储是 EF 实现，管理用例本身仍是 Core 的；扫描是集群任务，间隔取 DispatchInterval。</summary>
    [Fact]
    public void AddWebhooksEfCore_registers_the_publisher_reader_and_cluster_dispatch_job()
    {
        var services = new ServiceCollection().AddWebhooksEfCore<WebhookTestDbContext>();

        services.AssertSingle<IWebhookPublisher>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookPublisher, EfCoreWebhookPublisher<WebhookTestDbContext>>();
        services.AssertSingle<IWebhookDeliveryReader>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookDeliveryReader, EfCoreWebhookDeliveryReader<WebhookTestDbContext>>();
        services.AssertSingle<IWebhookManagementStore>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookManagementStore, EfCoreWebhookManagementStore<WebhookTestDbContext>>();
        services.AssertSingle<IWebhookManagementService>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookManagementService, WebhookManagementService>();

        using var provider = services
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Leistd:Webhooks:DispatchInterval"] = "00:00:30" })
                .Build())
            .BuildServiceProvider();
        var definition = provider.GetServices<RecurringJobDefinition>().Single(d => d.Name == "webhooks.dispatch");
        Assert.Equal(RecurringJobScope.Cluster, definition.Scope);
        Assert.Null(definition.IsEnabled);
        var schedule = definition.ScheduleFactory(provider);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(30), schedule.GetNextRun(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void AddWebhooksEfCore_is_idempotent()
        => ServiceCollectionAssertions.AssertIdempotent(services => services.AddWebhooksEfCore<WebhookTestDbContext>());

    /// <summary>webhook 表只映射在一个上下文里：以另一个上下文再调用时抛出，而不是按注册顺序静默取其一。</summary>
    [Fact]
    public void Registering_a_second_DbContext_is_rejected()
    {
        var services = new ServiceCollection().AddWebhooksEfCore<WebhookTestDbContext>();

        Assert.Throws<InvalidOperationException>(() => services.AddWebhooksEfCore<OtherDbContext>());
    }

    /// <summary>宿主先以另一配置节调用 AddWebhooks 时，EF 入口沿用它，不因默认配置节不同而抛出。</summary>
    [Fact]
    public void A_custom_section_bound_by_the_host_first_is_kept()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Custom:Webhooks:DispatchInterval"] = "00:01:00" })
                .Build())
            .AddWebhooks(configSectionPath: "Custom:Webhooks")
            .AddWebhooksEfCore<WebhookTestDbContext>()
            .BuildServiceProvider();

        Assert.Equal(TimeSpan.FromMinutes(1), provider.GetRequiredService<IOptions<WebhookOptions>>().Value.DispatchInterval);
    }

    /// <summary>按文档组合后，发布器、统计读取、管理用例与扫描任务都能在作用域里解析。</summary>
    [Fact]
    public async Task The_documented_registration_resolves_the_publisher_reader_and_jobs()
    {
        await using var host = await WebhookTestHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();

        Assert.IsType<EfCoreWebhookPublisher<WebhookTestDbContext>>(scope.ServiceProvider.GetRequiredService<IWebhookPublisher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWebhookDeliveryReader>());
        Assert.IsType<WebhookManagementService>(scope.ServiceProvider.GetRequiredService<IWebhookManagementService>());
        foreach (var definition in host.Services.GetServices<RecurringJobDefinition>())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(definition.JobType));
        }
    }

    private sealed class OtherDbContext(DbContextOptions<OtherDbContext> options) : DbContext(options);
}
