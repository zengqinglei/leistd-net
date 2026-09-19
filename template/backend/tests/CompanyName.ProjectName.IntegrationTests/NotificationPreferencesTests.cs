#if (LocalIdentity)
#if (IncludeNotifications)
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Notifications;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Email.Abstractions;
using Leistd.Notifications.Abstractions;
using Leistd.Notifications.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 通知偏好与安全提醒：本人按"类别 × 渠道"决定收什么；安全提醒的站内通知关不掉；邮件只发已验证的邮箱。
/// </summary>
public sealed class NotificationPreferencesTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "NotifyTests!Passw0rd";

    [Fact]
    public async Task Password_change_sends_an_in_app_security_alert()
    {
        var (host, _) = CreateHost();
        using var _host = host;
        var (username, _) = await CreateUserAsync(host, "notify_pwd");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        await ChangePasswordAsync(session.Client);

        var notifications = await ReadNotificationsAsync(session.Client);
        var alert = Assert.Single(notifications, n => n.GetProperty("type").GetString() == "Security");
        // 文案必须已渲染：取不到词条时本地化器原样返回键
        Assert.DoesNotContain("SecurityAlert:", alert.GetProperty("title").GetString());
        Assert.DoesNotContain("SecurityAlert:", alert.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Security_alert_email_goes_only_to_a_verified_address()
    {
        var (host, mailbox) = CreateHost();
        using var _host = host;
        var (username, email) = await CreateUserAsync(host, "notify_mail");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        // 邮箱未验证：不发
        await ChangePasswordAsync(session.Client, Password, Password + "1");
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(mailbox.Received.ContainsKey(email));

        await ConfirmEmailAsync(host, username);
        await ChangePasswordAsync(session.Client, Password + "1", Password + "2");

        Assert.True(await mailbox.WaitForAsync(email, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Disabling_in_app_system_notifications_keeps_security_alerts()
    {
        var (host, _) = CreateHost();
        using var _host = host;
        var (username, _) = await CreateUserAsync(host, "notify_off");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        using (var off = await session.Client.PutAsJsonAsync(
                   "/api/v1/settings/current-user",
                   new { Name = SettingConstant.Notifications.SystemInApp, Value = "false" }))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }

        var userId = (await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetString()!;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishToUserAsync(
                userId,
                new NotificationInputDto { Title = "System notice", Type = AppNotificationTypes.System });
        }

        await ChangePasswordAsync(session.Client);

        var types = (await ReadNotificationsAsync(session.Client)).Select(n => n.GetProperty("type").GetString()).ToList();
        Assert.DoesNotContain(AppNotificationTypes.System, types);
        Assert.Contains("Security", types);
    }

    private (WebApplicationFactory<Program> Host, CapturingMailbox Mailbox) CreateHost()
    {
        var mailbox = new CapturingMailbox();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(mailbox);
        }));
        return (host, mailbox);
    }

    private static async Task ChangePasswordAsync(HttpClient client, string current = Password, string next = Password + "x")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            CurrentPassword = current,
            NewPassword = next,
            ConfirmPassword = next
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<JsonElement>> ReadNotificationsAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/notifications"));
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task ConfirmEmailAsync(WebApplicationFactory<Program> host, string username)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var user = await db.Set<User>().SingleAsync(u => u.Username == username);
        user.ConfirmEmail();
        await db.SaveChangesAsync();
    }

    private static async Task<(string Username, string Email)> CreateUserAsync(WebApplicationFactory<Program> host, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        var email = $"{username}@example.test";
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = email,
            Password,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        return (username, email);
    }

    /// <summary>记下收到的信；邮件经后台队列发送，断言要等。</summary>
    private sealed class CapturingMailbox : IEmailSender
    {
        public ConcurrentDictionary<string, EmailMessage> Received { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Received[message.To] = message;
            return Task.CompletedTask;
        }

        public async Task<bool> WaitForAsync(string to, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Received.ContainsKey(to))
                    return true;
                await Task.Delay(100);
            }

            return false;
        }
    }
}
#endif
#endif
