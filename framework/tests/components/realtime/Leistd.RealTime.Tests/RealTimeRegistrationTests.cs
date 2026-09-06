using Leistd.RealTime.Abstractions;
using Leistd.RealTime.AspNetCore.SignalR;
using Leistd.RealTime.AspNetCore.SignalR.Services;
using Leistd.RealTime.Options;
using Leistd.Security.Claims;
using Leistd.TestBase.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.RealTime.Tests;

/// <summary>
/// 实时组件的注册面与端点映射。
/// </summary>
public class RealTimeRegistrationTests
{
    private static IServiceCollection Base() => new ServiceCollection().AddLogging();

    [Fact]
    public void Registration_exposes_the_business_event_publisher()
    {
        var services = Base();

        services.AddRealTimeSignalR();

        services.AssertSingle<IBusinessEventPublisher>(ServiceLifetime.Singleton);
        services.AssertResolvesTo<IBusinessEventPublisher, SignalRBusinessEventPublisher>();
    }

    // 走 SignalR 基座而不是裸 AddSignalR：Hub 方法调用不经中间件，
    // 主体/租户/链路标识与 UserIdentifier 解析全靠基座。
    [Fact]
    public void Registration_brings_in_the_signalr_ambient_context_base()
    {
        var services = Base();

        services.AddRealTimeSignalR();

        services.AssertSingle<IUserIdProvider>(ServiceLifetime.Singleton);
    }

    // 通知与实时常常同时装，两者都会调基座。不幂等会让过滤器挂两遍，
    // 每次 Hub 调用建立两层环境上下文并复评两遍。
    [Fact]
    public void Registration_is_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(services =>
        {
            services.AddLogging();
            services.AddRealTimeSignalR();
        });
    }

    [Fact]
    public void Options_delegate_is_applied()
    {
        using var provider = Base()
            .AddRealTimeSignalR(o => o.RealTimeHubPath = "/custom/hub")
            .BuildServiceProvider();

        Assert.Equal("/custom/hub", provider.GetRequiredService<IOptions<RealTimeOptions>>().Value.RealTimeHubPath);
    }

    [Fact]
    public void Options_have_a_default_hub_path_without_a_delegate()
    {
        using var provider = Base().AddRealTimeSignalR().BuildServiceProvider();

        Assert.False(string.IsNullOrWhiteSpace(
            provider.GetRequiredService<IOptions<RealTimeOptions>>().Value.RealTimeHubPath));
    }

    // 授权器缺失必须在映射端点时就失败关闭，并指出该注册什么。
    // 拖到首次订阅才失败太晚，且错误信息离现场很远。
    [Fact]
    public void Mapping_without_an_authorizer_fails_with_actionable_guidance()
    {
        var app = BuildApp(services => services.AddRealTimeSignalR());

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapRealTimeHub());

        Assert.Contains(nameof(IRealTimeSubscriptionAuthorizer), ex.Message);
        Assert.Contains("AddAllowAllRealTimeSubscriptions", ex.Message);
    }

    [Fact]
    public void Mapping_succeeds_once_an_authorizer_is_registered()
    {
        var app = BuildApp(services =>
        {
            services.AddRealTimeSignalR();
            services.AddSingleton<IRealTimeSubscriptionAuthorizer, AllowAllAuthorizer>();
        });

        Assert.Same(app, app.MapRealTimeHub());
    }

    private sealed class AllowAllAuthorizer : IRealTimeSubscriptionAuthorizer
    {
        public Task<bool> AuthorizeAsync(
            RealTimeSubscriptionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static WebApplication BuildApp(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        configure(builder.Services);
        builder.Services.AddAuthorization();
        return builder.Build();
    }
}
