#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class SessionLifetimeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 720)]
    public async Task Both_tiers_issue_persistent_cookies_with_a_fixed_expiration(bool keepSignedIn, int hours)
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        var start = clock.GetUtcNow();
        using var session = await SignInAsync(host, keepSignedIn, start.AddHours(hours));
        clock.Advance(TimeSpan.FromHours(23));
        using var reopened = ProjectWebApplicationFactory.CreateProjectClient(host);
        reopened.DefaultRequestHeaders.Add("Cookie", session.Cookie);
        using var active = await reopened.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.Contains("Set-Cookie"));

        if (!keepSignedIn)
        {
            clock.Advance(TimeSpan.FromHours(1));
            using var expired = await reopened.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
    }

    [Fact]
    public async Task Active_keep_signed_in_sessions_end_at_the_thirty_day_boundary()
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        using var session = await SignInAsync(host, keepSignedIn: true, clock.GetUtcNow().AddDays(30));
        for (var period = 0; period < 4; period++)
        {
            clock.Advance(TimeSpan.FromDays(6));
            using var active = await session.Client.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }

        clock.Advance(TimeSpan.FromDays(5));
        using var last = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        clock.Advance(TimeSpan.FromDays(1));
        using var expired = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Keep_signed_in_sessions_end_at_the_idle_boundary()
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        using var session = await SignInAsync(host, keepSignedIn: true, clock.GetUtcNow().AddDays(30));

        clock.Advance(TimeSpan.FromDays(7));
        using var expired = await session.Client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Early_activity_counts_past_the_old_cookie_deadline()
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        using var session = await SignInAsync(host, keepSignedIn: true, clock.GetUtcNow().AddDays(30));
        clock.Advance(TimeSpan.FromDays(7 * 0.4));
        using var early = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        Assert.False(early.Headers.Contains("Set-Cookie"));
        clock.Advance(TimeSpan.FromDays(7 * 0.7));

        using var later = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Theory]
    [InlineData(SettingConstant.Security.AllowKeepSignedIn, "false")]
    [InlineData(SettingConstant.Security.SessionIdleTimeoutDays, "1")]
    public async Task Existing_sessions_obey_tighter_tenant_policy(string setting, string value)
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        using var session = await SignInAsync(host, keepSignedIn: true, clock.GetUtcNow().AddDays(30));
        clock.Advance(TimeSpan.FromDays(2));
        await ProjectWebApplicationFactory.SetTenantSettingAsync(host, setting, value);
        try
        {
            using var expired = await session.Client.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
        finally
        {
            await ProjectWebApplicationFactory.SetTenantSettingAsync(host, setting, null);
        }
    }

    [Theory]
    [InlineData(SettingConstant.Security.SessionIdleTimeoutDays, "8")]
    [InlineData(SettingConstant.Security.RememberDeviceDays, "91")]
    public async Task Tenant_writes_above_the_deployment_limit_are_rejected(string setting, string value)
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/current-tenant", new { Name = setting, Value = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upgrading_an_existing_session_uses_its_creation_time_and_keeps_the_long_tier()
    {
        using var isolated = new ProjectWebApplicationFactory();
        using var login = await isolated.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var sessions = await login.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me/sessions");
        var id = sessions.EnumerateArray().Single(row => row.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id").GetGuid();

        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, migration => migration.EndsWith("_AddUserSessionPolicy", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        await migrator.MigrateAsync(migrations[index]);

        var upgraded = await db.UserSessions.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.True(upgraded.KeepSignedIn);
        Assert.Equal(upgraded.CreationTime, upgraded.AuthenticatedAt);
    }

#if (Impersonation)
    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 720)]
    public async Task Entering_and_leaving_impersonation_keep_the_original_expiration(bool keepSignedIn, int hours)
    {
        var (host, clock) = CreateHost();
        using var lifetime = host;
        var expires = clock.GetUtcNow().AddHours(hours);
        using var admin = await SignInAsync(host, keepSignedIn, expires);
        var name = $"session-{Guid.NewGuid():N}"[..20];
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            AdminEmail = $"admin@{name}.example.test",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromHours(1));

        using var enter = await admin.Client.PostAsync($"/api/v1/tenants/{id}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, enter.StatusCode);
        AssertExpiration(enter, expires);
        using var impersonated = ProjectWebApplicationFactory.CreateProjectClient(host);
        impersonated.DefaultRequestHeaders.Add("Cookie", CookieOf(enter));
        clock.Advance(TimeSpan.FromHours(1));
        using var exit = await impersonated.PostAsync("/api/v1/auth/end-impersonation", null);
        Assert.Equal(HttpStatusCode.OK, exit.StatusCode);
        AssertExpiration(exit, expires);
    }
#endif

    private (WebApplicationFactory<Program> Host, FakeTimeProvider Clock) CreateHost()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        return (host, clock);
    }

    private static async Task<AuthenticatedSession> SignInAsync(WebApplicationFactory<Program> host, bool keepSignedIn, DateTimeOffset expires)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/session-login", new
        {
            UsernameOrEmail = "admin",
            Password = ProjectWebApplicationFactory.TestAdminPassword,
            KeepSignedIn = keepSignedIn
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertExpiration(login, expires);
        var cookie = CookieOf(login);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new AuthenticatedSession(client, cookie);
    }

    private static void AssertExpiration(HttpResponseMessage response, DateTimeOffset expires)
    {
        var cookie = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
            .Single(value => value.Name.Value!.EndsWith(".Auth", StringComparison.Ordinal) && value.Expires is not null);
        Assert.Equal(expires, cookie.Expires);
    }

    private static string CookieOf(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
}
#endif
