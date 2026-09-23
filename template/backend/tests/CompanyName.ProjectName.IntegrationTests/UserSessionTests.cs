#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 登录设备：每次登录登记一个会话，撤销对已发出的 Cookie 立即生效。
/// </summary>
/// <remarks>
/// 断言都落在"那份 Cookie 还能不能用"上：会话表里少一行不算数，Cookie 仍被放行才是要防的事。
/// </remarks>
public sealed class UserSessionTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "SessionTests!Passw0rd";

    [Fact]
    public async Task Each_sign_in_registers_a_session_with_the_current_one_first()
    {
        var username = await CreateUserAsync("sess_list");
        using var first = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        using var second = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        var sessions = await ReadSessionsAsync(second.Client);

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].GetProperty("isCurrent").GetBoolean());
        Assert.False(sessions[1].GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task Revoked_device_cookie_stops_working_immediately()
    {
        var username = await CreateUserAsync("sess_one");
        using var mine = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        using var other = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        var otherId = (await ReadSessionsAsync(other.Client)).Single(s => s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id").GetGuid();

        using var revoke = await mine.Client.DeleteAsync($"/api/v1/auth/me/sessions/{otherId}");

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Revoke_endpoint_cannot_end_the_current_session()
    {
        var username = await CreateUserAsync("sess_self");
        using var mine = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        var currentId = (await ReadSessionsAsync(mine.Client)).Single().GetProperty("id").GetGuid();

        using var revoke = await mine.Client.DeleteAsync($"/api/v1/auth/me/sessions/{currentId}");

        Assert.Equal(HttpStatusCode.Conflict, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cannot_revoke_another_users_session()
    {
        var victim = await CreateUserAsync("sess_victim");
        using var victimSession = await ProjectWebApplicationFactory.LoginAsync(factory, victim, Password);
        var victimSessionId = (await ReadSessionsAsync(victimSession.Client)).Single().GetProperty("id").GetGuid();

        var attacker = await CreateUserAsync("sess_attacker");
        using var attackerSession = await ProjectWebApplicationFactory.LoginAsync(factory, attacker, Password);
        using var revoke = await attackerSession.Client.DeleteAsync($"/api/v1/auth/me/sessions/{victimSessionId}");

        // 静默成功：不借此透露别人的会话 Id 是否存在；但对方的会话必须还在
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await victimSession.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_other_devices_keeps_only_the_current_session()
    {
        var username = await CreateUserAsync("sess_others");
        using var a = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        using var b = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        using var mine = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        using var revoke = await mine.Client.PostAsync("/api/v1/auth/me/sessions/revoke-others", null);

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Single(await ReadSessionsAsync(mine.Client));
    }

    [Fact]
    public async Task Password_change_signs_out_other_devices()
    {
        var username = await CreateUserAsync("sess_pwd");
        using var other = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        using var mine = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        using var change = await mine.Client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            CurrentPassword = Password,
            NewPassword = Password + "2",
            ConfirmPassword = Password + "2"
        });

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_password_reset_ends_all_user_sessions()
    {
        var username = await CreateUserAsync("sess_reset");
        using var target = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);
        var targetId = await ReadUserIdAsync(target.Client);

        using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var reset = await admin.Client.PostAsJsonAsync($"/api/v1/users/{targetId}/reset-password", new { Password = Password + "3" });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Copied_cookie_is_rejected_after_sign_out()
    {
        var username = await CreateUserAsync("sess_logout");
        using var mine = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        // 退出前复制一份 Cookie：只删浏览器里的 Cookie 挡不住这份副本
        using var copy = ProjectWebApplicationFactory.CreateProjectClient(factory);
        copy.DefaultRequestHeaders.Add("Cookie", mine.Cookie);

        using var logout = await mine.Client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.GetAsync("/api/v1/auth/me")).StatusCode);
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

    private static async Task<List<JsonElement>> ReadSessionsAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/auth/me/sessions"));
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<Guid> ReadUserIdAsync(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/v1/auth/me"));
        return me.RootElement.GetProperty("id").GetGuid();
    }
}
#endif
