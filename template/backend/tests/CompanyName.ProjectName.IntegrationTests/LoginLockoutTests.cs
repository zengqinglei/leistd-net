#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>登录失败锁定：按租户设置的阈值锁定、锁定期间不校验密码、已登录会话不受影响、管理员可提前解除。</summary>
/// <remarks>用例在同一个夹具里顺序执行；改设置的用例在结束时清掉覆盖值，回到默认的 5 次 / 15 分钟。</remarks>
public sealed class LoginLockoutTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "LockoutTests!Passw0rd";
    private const string WrongPassword = "LockoutTests!Wrong000";

    [Fact]
    public async Task Existing_sessions_are_not_affected_by_lockout()
    {
        var username = await CreateUserAsync("lock_session");
        using var signedIn = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        await LockOutAsync(username);

        Assert.Equal(HttpStatusCode.OK, (await signedIn.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_unlock_allows_sign_in_after_the_configured_threshold_locks_the_user()
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        await WriteSettingAsync(admin.Client, SettingConstant.Security.LockoutMaxFailedAttempts, "2");
        try
        {
            var username = await CreateUserAsync("lock_unlock");
            await LockOutAsync(username, 2);
            var locked = await FindUserAsync(admin.Client, username);
            Assert.True(locked.GetProperty("isLockedOut").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null, locked.GetProperty("lockoutEnd").ValueKind);
            Assert.Equal("Auth:UserTemporarilyLockedOut", await LoginErrorCodeAsync(username, Password));

            using var unlock = await admin.Client.PostAsync($"/api/v1/users/{locked.GetProperty("id").GetGuid()}/unlock", null);
            Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);

            Assert.False((await FindUserAsync(admin.Client, username)).GetProperty("isLockedOut").GetBoolean());
            using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        }
        finally
        {
            await WriteSettingAsync(admin.Client, SettingConstant.Security.LockoutMaxFailedAttempts, null);
        }
    }

    [Fact]
    public async Task Rejects_a_zero_lockout_duration()
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        using var rejected = await admin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Security.LockoutDurationMinutes, Value = "0" });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private async Task LockOutAsync(
        string username, int threshold = SettingConstant.Security.DefaultLockoutMaxFailedAttempts)
    {
        for (var i = 1; i < threshold; i++)
        {
            Assert.Equal("Auth:InvalidCredentials", await LoginErrorCodeAsync(username, WrongPassword));
        }

        Assert.Equal("Auth:UserTemporarilyLockedOut", await LoginErrorCodeAsync(username, WrongPassword));
    }

    private async Task<string?> LoginErrorCodeAsync(string username, string password)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = username, Password = password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<string> CreateUserAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = $"{username}@example.test",
            Password,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        return username;
    }

    private static async Task<JsonElement> FindUserAsync(HttpClient client, string username)
    {
        using var body = JsonDocument.Parse(
            await client.GetStringAsync($"/api/v1/users?offset=0&limit=10&keyword={username}"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == username)
            .Clone();
    }

    private static async Task WriteSettingAsync(HttpClient client, string name, string? value)
    {
        using var response = await client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant", new { Name = name, Value = value });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
#endif
