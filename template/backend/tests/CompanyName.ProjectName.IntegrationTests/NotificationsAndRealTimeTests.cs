#if (IncludeNotifications)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Notifications;
using Leistd.RealTime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class NotificationsAndRealTimeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Notification_should_be_persisted_pushed_and_marked_as_read()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var userId = await GetSuperAdminIdAsync(factory);
        await using var connection = CreateHubConnection(factory, "/hubs/notifications", admin.Cookie);
        var received = new TaskCompletionSource<NotificationOutputDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On<NotificationOutputDto>("NotificationReceived", notification => received.TrySetResult(notification));
        await connection.StartAsync();

        var notification = new NotificationOutputDto
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
        Assert.Equal(notification.Id, pushed.Id);
        Assert.Equal(1, await admin.Client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count"));

        var notifications = await admin.Client.GetFromJsonAsync<List<NotificationOutputDto>>("/api/v1/notifications");
        Assert.Contains(notifications!, item => item.Id == notification.Id && !item.IsRead);

        var markRead = await admin.Client.PutAsync($"/api/v1/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, markRead.StatusCode);
        Assert.Equal(0, await admin.Client.GetFromJsonAsync<int>("/api/v1/notifications/unread-count"));

        // 显式停止连接，排空 SignalR 后台循环（持有 CTS），避免与 await using 释放竞争导致类清理期 ObjectDisposedException
        await connection.StopAsync();
    }

    [Fact]
    public async Task Default_realtime_subscription_should_keep_common_resources_available()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        Assert.False(factory.Services.GetRequiredService<IOptions<RealTimeOptions>>().Value.RequireSubscriptionAuthorization);

        await using var connection = CreateHubConnection(factory, "/hubs/realtime", admin.Cookie);
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

    [Fact]
    public async Task Enabled_subscription_authorization_should_allow_common_and_reject_forbidden_resources()
    {
        await using var securedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRealtimeSubscriptionAuthorizer>();
                services.AddSingleton<IRealtimeSubscriptionAuthorizer, PrefixSubscriptionAuthorizer>();
                services.PostConfigure<RealTimeOptions>(options => options.RequireSubscriptionAuthorization = true);
            }));
        using var admin = await LoginAsync(securedFactory, "admin", "Admin@123456");
        await using var connection = CreateHubConnection(securedFactory, "/hubs/realtime", admin.Cookie);
        await connection.StartAsync();

        await connection.InvokeAsync("Subscribe", "public:announcements");
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            connection.InvokeAsync("Subscribe", "private:42"));
        Assert.Contains("Subscription forbidden", exception.ToString(), StringComparison.OrdinalIgnoreCase);

        // securedFactory 是派生工厂，须在连接后台循环排空后再随 await using 释放，否则类清理期出现 CTS 已释放的竞争
        await connection.StopAsync();
    }

    private static HubConnection CreateHubConnection(
        WebApplicationFactory<Program> application,
        string path,
        string cookie)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                options.Headers.Add("Cookie", cookie);
            })
            .Build();
    }

    private static async Task<AuthenticatedSession> LoginAsync(
        WebApplicationFactory<Program> application,
        string username,
        string password)
    {
        var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = username, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new AuthenticatedSession(client, cookie);
    }

    private static async Task<Guid> GetSuperAdminIdAsync(WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Users.Where(user => user.IsSuperAdmin).Select(user => user.Id).SingleAsync();
    }

    private sealed record BusinessEvent(string Value);

    private sealed class PrefixSubscriptionAuthorizer : IRealtimeSubscriptionAuthorizer
    {
        public Task<bool> AuthorizeAsync(
            RealtimeSubscriptionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(context.ResourceKey.StartsWith("public:", StringComparison.Ordinal));
        }
    }
}
#endif
