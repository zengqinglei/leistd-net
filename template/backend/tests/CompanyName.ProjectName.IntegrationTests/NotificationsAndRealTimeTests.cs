#if (IncludeNotifications)
using Leistd.Notifications;
using CompanyName.ProjectName.Api.Middlewares;
using Leistd.Notifications.Dtos;
using System.Net;
using System.Net.Http.Json;
#if (!LocalIdentity)
using CompanyName.ProjectName.Api.Extensions;
using Microsoft.AspNetCore.Http;
#endif
using CompanyName.ProjectName.Api.RealTime;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Notifications.Abstractions;
using Leistd.RealTime.Abstractions;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class NotificationsAndRealTimeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
#if (!LocalIdentity)
    [Fact]
    public async Task SignalR_query_token_is_promoted_only_on_known_hub_paths()
    {
        var hub = new DefaultHttpContext();
        hub.Request.Path = "/hubs/notifications";
        hub.Request.QueryString = new QueryString("?access_token=secret&transport=WebSockets");
        var middleware = new HubAccessTokenMiddleware(context =>
        {
            Assert.Equal("Bearer secret", context.Request.Headers.Authorization);
            Assert.False(context.Request.Query.ContainsKey("access_token"));
            Assert.Equal("WebSockets", context.Request.Query["transport"]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(hub);

        var api = new DefaultHttpContext();
        api.Request.Path = "/api/v1/notifications";
        api.Request.QueryString = new QueryString("?access_token=secret");
        middleware = new HubAccessTokenMiddleware(context =>
        {
            Assert.False(context.Request.Headers.ContainsKey("Authorization"));
            Assert.Equal("secret", context.Request.Query["access_token"]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(api);
    }
#endif

    [Fact]
    public async Task Notification_should_be_persisted_pushed_marked_as_read_and_cleared()
    {
#if (LocalIdentity)
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var userId = await GetSuperAdminIdAsync(factory);
#else
        var userId = Guid.CreateVersion7();
        using var admin = factory.CreateResourceSession(userId, Guid.CreateVersion7());
#endif
        await using var connection = CreateHubConnection(factory, "/hubs/notifications", admin);
        var received = new TaskCompletionSource<NotificationOutputDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On<NotificationOutputDto>("NotificationReceived", notification => received.TrySetResult(notification));
        await connection.StartAsync();

        var notification = new NotificationInputDto
        {
            Title = "Integration notification",
            Content = "Notification persistence and SignalR delivery"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await publisher.PublishToUserAsync(userId.ToString(), notification);
        }

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, await admin.Client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count"));

        // 身份由发布器在收件人边界定案，调用方并不知道它——因此这里断言的是那条保证本身：
        // 推送里的 ID 必须就是落库那条的 ID，否则客户端拿推送的 ID 去标记已读会命不中。
        var notifications = await admin.Client.GetFromJsonAsync<List<NotificationOutputDto>>("/api/v1/notifications");
        var stored = Assert.Single(notifications!);
        Assert.Equal(stored.Id, pushed.Id);
        Assert.False(stored.IsRead);
        Assert.Equal(notification.Title, stored.Title);
        var notificationId = stored.Id;

        var markRead = await admin.Client.PutAsync($"/api/v1/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.OK, markRead.StatusCode);
        Assert.Equal(0, await admin.Client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count"));

        // 删除单条：持久删除指定通知
        var clearOne = await admin.Client.DeleteAsync($"/api/v1/notifications/{notificationId}");
        Assert.Equal(HttpStatusCode.OK, clearOne.StatusCode);

        var afterClearOne = await admin.Client.GetFromJsonAsync<List<NotificationOutputDto>>("/api/v1/notifications");
        Assert.DoesNotContain(afterClearOne!, item => item.Id == notificationId);

        // 清空全部：持久删除当前用户的通知记录
        var clearAll = await admin.Client.DeleteAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, clearAll.StatusCode);

        var afterClear = await admin.Client.GetFromJsonAsync<List<NotificationOutputDto>>("/api/v1/notifications");
        Assert.Empty(afterClear!);

        // 显式停止连接，排空 SignalR 后台循环（持有 CTS），避免与 await using 释放竞争导致类清理期 ObjectDisposedException
        await connection.StopAsync();
    }

    [Fact]
    public async Task Default_realtime_subscription_should_keep_common_resources_available()
    {
#if (LocalIdentity)
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
#else
        using var admin = factory.CreateResourceSession(Guid.CreateVersion7(), Guid.CreateVersion7());
#endif
        // 开关已移除：订阅授权无条件生效，模板默认只放行 public: 命名空间。
        Assert.IsType<PublicResourceSubscriptionAuthorizer>(
            factory.Services.GetRequiredService<IRealTimeSubscriptionAuthorizer>());

        await using var connection = CreateHubConnection(factory, "/hubs/realtime", admin);
        var received = new TaskCompletionSource<BusinessEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On<BusinessEvent>("BusinessEvent", message => received.TrySetResult(message));
        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", "public:announcements");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IBusinessEventPublisher>();
            await publisher.PublishToResourceAsync("public:announcements", "BusinessEvent", new BusinessEvent("available"));
        }

        Assert.Equal("available", (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Value);

        await connection.StopAsync();
    }

    // 默认授权器就会拒绝非 public: 的 key：客户端能给任意字符串，组名又不含租户段，
    // 放行任意 key 等于允许已认证用户订阅别的租户的资源。
    [Fact]
    public async Task Default_subscription_authorization_should_allow_public_and_reject_other_resources()
    {
#if (LocalIdentity)
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
#else
        using var admin = factory.CreateResourceSession(Guid.CreateVersion7(), Guid.CreateVersion7());
#endif
        await using var connection = CreateHubConnection(factory, "/hubs/realtime", admin);
        await connection.StartAsync();

        await connection.InvokeAsync("Subscribe", "public:announcements");
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.InvokeAsync("Subscribe", "private:42"));
        Assert.Contains("Subscription forbidden", exception.ToString(), StringComparison.OrdinalIgnoreCase);

        await connection.StopAsync();
    }

    private static HubConnection CreateHubConnection(
        WebApplicationFactory<Program> application,
        string path,
        AuthenticatedSession session)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                if (!string.IsNullOrEmpty(session.Cookie))
                    options.Headers.Add("Cookie", session.Cookie);
                foreach (var (name, value) in session.AuthenticationHeaders)
                    options.Headers.Add(name, value);
            })
            .Build();
    }


    private static async Task<Guid> GetSuperAdminIdAsync(WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Users.Where(user => user.IsSuperAdmin).Select(user => user.Id).SingleAsync();
    }

    private sealed record BusinessEvent(string Value);

}
#endif
