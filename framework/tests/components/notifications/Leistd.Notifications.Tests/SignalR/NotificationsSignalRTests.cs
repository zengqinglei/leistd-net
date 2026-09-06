using Leistd.Notifications.Abstractions;
using Leistd.Notifications.AspNetCore.SignalR;
using Leistd.Notifications.AspNetCore.SignalR.Hubs;
using Leistd.Notifications.AspNetCore.SignalR.Services;
using Leistd.Notifications.Dtos;
using Leistd.Security.Claims;
using Leistd.TestBase.Assertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.Notifications.Tests.SignalR;

/// <summary>
/// 通知的 SignalR 传输：注册面与投递寻址。
/// </summary>
public class NotificationsSignalRTests
{
    private static NotificationOutputDto Notification() => new()
    {
        Id = Guid.CreateVersion7().ToString("N"),
        Title = "t",
        Type = "System",
        CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Registration_exposes_the_signalr_sender()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddNotificationsSignalR()
            .BuildServiceProvider();

        Assert.Single(provider.GetServices<INotificationSender>());
        Assert.IsType<SignalRNotificationSender>(provider.GetServices<INotificationSender>().Single());
    }

    /// <summary>SignalR 传输与宿主自己的传输通道并存，重复注册也只有一条。</summary>
    /// <remarks>
    /// <see cref="INotificationSender"/> 是累加型扩展点：发布器以 <c>IEnumerable&lt;T&gt;</c>
    /// 注入并逐一调用，宿主同时装邮件、WebPush 是设计内的用法（随包文档亦如此承诺）。
    /// 按服务类型判重会让"宿主已装了别的通道"变成"SignalR 这一路静默消失"——
    /// 不报错，只是推送再也不到达，因此必须按实现类型判重。
    /// </remarks>
    [Fact]
    public void Signalr_sender_coexists_with_host_transports_and_stays_single_on_repeat()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<INotificationSender, HostEmailSender>();

        services.AddNotificationsSignalR();
        services.AddNotificationsSignalR();

        using var provider = services.BuildServiceProvider();
        var senders = provider.GetServices<INotificationSender>().ToArray();

        Assert.Equal(2, senders.Length);
        Assert.Single(senders, s => s is HostEmailSender);
        Assert.Single(senders, s => s is SignalRNotificationSender);
    }

    private sealed class HostEmailSender : INotificationSender
    {
        public Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // 传输包必须把 SignalR 基座一起带上：Hub 方法调用不经中间件，
    // 主体/租户/链路标识全靠基座的过滤器与 UserIdProvider。
    [Fact]
    public void Registration_brings_in_the_signalr_ambient_context_base()
    {
        var services = new ServiceCollection().AddLogging();

        services.AddNotificationsSignalR();

        services.AssertSingle<IUserIdProvider>(ServiceLifetime.Singleton);
    }

    // 通知与实时是两个包，宿主常常两个都装。基座注册必须幂等，
    // 否则同一个过滤器挂两遍，每次 Hub 调用建立两层环境上下文。
    [Fact]
    public void Registering_alongside_realtime_style_repeat_calls_stays_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(services =>
        {
            services.AddLogging();
            services.AddNotificationsSignalR();
        });
    }

    [Fact]
    public async Task Notifications_are_addressed_by_user_identifier_not_by_group()
    {
        var clients = new RecordingHubClients();
        var sender = new SignalRNotificationSender(
            new StubHubContext(clients), NullLogger<SignalRNotificationSender>.Instance);
        var notification = Notification();

        await sender.SendToUserAsync("user-1", notification);

        var (userId, method, payload) = Assert.Single(clients.Sent);
        Assert.Equal("user-1", userId);
        Assert.Equal("NotificationReceived", method);
        Assert.Same(notification, payload);
        Assert.Empty(clients.GroupSends);
    }

    // 投递失败不得冒泡：通知是尽力而为的旁路，推送挂掉不能让业务写入回滚。
    // 持久化已经完成，用户下次拉列表仍能看到。
    [Fact]
    public async Task Transport_failure_is_swallowed_so_the_business_call_survives()
    {
        var clients = new RecordingHubClients { Throw = new InvalidOperationException("hub down") };
        var sender = new SignalRNotificationSender(
            new StubHubContext(clients), NullLogger<SignalRNotificationSender>.Instance);

        await sender.SendToUserAsync("user-1", Notification());

        Assert.Empty(clients.Sent);
    }

    /// <summary>Hub 端点必须要求登录。</summary>
    /// <remarks>
    /// 漏掉 <c>RequireAuthorization()</c> 的表现是任何匿名连接都能连上通知 Hub 并订阅，
    /// 而端点本身照常工作——没有任何报错，只有越权读。
    /// </remarks>
    [Fact]
    public void Mapped_hub_endpoint_requires_an_authenticated_caller()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddNotificationsSignalR();
        builder.Services.AddAuthorization();
        var app = builder.Build();

        app.MapNotificationHub();

        // 从 builder 自己的数据源读，而不是容器里的复合 EndpointDataSource——
        // 后者要等应用真正启动后才填充
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToArray();
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e => Assert.NotNull(e.Metadata.GetMetadata<IAuthorizeData>()));
    }

    [Fact]
    public void Hub_path_default_is_stable()
    {
        // 前端连接串写死了这个路径，改它是破坏性变更
        Assert.Equal(
            "/hubs/notifications",
            Leistd.Notifications.AspNetCore.SignalR.DependencyInjection.DefaultNotificationHubPath);
    }

    private sealed class StubHubContext(IHubClients clients) : IHubContext<NotificationHub>
    {
        public IHubClients Clients { get; } = clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<(string UserId, string Method, object? Payload)> Sent { get; } = [];
        public List<string> GroupSends { get; } = [];
        public Exception? Throw { get; init; }

        public IClientProxy User(string userId) => new RecordingProxy(userId, this);

        public IClientProxy Group(string groupName)
        {
            GroupSends.Add(groupName);
            return new RecordingProxy(groupName, this);
        }

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        private sealed class RecordingProxy(string target, RecordingHubClients owner) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (owner.Throw is not null)
                {
                    throw owner.Throw;
                }

                owner.Sent.Add((target, method, args.FirstOrDefault()));
                return Task.CompletedTask;
            }
        }
    }
}
