using Leistd.TestBase.Assertions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Leistd.Webhooks.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary><c>AddWebhooks</c> 的注册面：发送器、签名、结果分类、管理用例与命名客户端。</summary>
public sealed class WebhookRegistrationTests
{
    [Fact]
    public void AddWebhooks_registers_the_default_sender_signer_and_classifier()
    {
        var services = new ServiceCollection().AddWebhooks();

        services.AssertSingle<IWebhookSender>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookSender, WebhookHttpSender>();
        services.AssertSingle<WebhookSigner>(ServiceLifetime.Singleton);
        services.AssertSingle<WebhookAttemptClassifier>(ServiceLifetime.Singleton);
        services.AssertSingle<WebhookNetworkGuard>(ServiceLifetime.Singleton);
    }

    /// <summary>管理用例与密钥保护属于 Core：不装存储也已注册，存储由适配包另行提供。</summary>
    [Fact]
    public void AddWebhooks_registers_the_management_use_cases()
    {
        var services = new ServiceCollection().AddWebhooks();

        services.AssertSingle<IWebhookManagementService>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IWebhookManagementService, WebhookManagementService>();
        services.AssertSingle<WebhookSecretProtector>(ServiceLifetime.Singleton);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWebhookManagementStore));
    }

    [Fact]
    public void AddWebhooks_is_idempotent()
        => ServiceCollectionAssertions.AssertIdempotent(services => services.AddWebhooks());

    /// <summary>命名客户端的配置是追加式的：重复调用不能把主处理器工厂登记两遍。</summary>
    [Fact]
    public void AddWebhooks_configures_the_named_client_once()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddWebhooks()
            .AddWebhooks()
            .BuildServiceProvider();

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>()
            .Get(WebhookHttpSender.HttpClientName);

        Assert.Single(factoryOptions.HttpMessageHandlerBuilderActions);
        Assert.Single(factoryOptions.HttpClientActions);
    }

    /// <summary>宿主替换发送器（如托管投递服务）时，先注册或之后 Replace 都生效。</summary>
    [Fact]
    public void A_sender_registered_by_the_host_first_is_kept()
    {
        var services = new ServiceCollection()
            .AddSingleton<IWebhookSender, HostSender>()
            .AddWebhooks();

        services.AssertSingle<IWebhookSender>(ServiceLifetime.Singleton);
        services.AssertImplementedBy<IWebhookSender, HostSender>();
    }

    private sealed class HostSender : IWebhookSender
    {
        public Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
