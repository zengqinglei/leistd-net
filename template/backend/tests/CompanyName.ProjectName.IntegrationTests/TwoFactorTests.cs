#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;
using CompanyName.ProjectName.Domain.Shared.Text;
using Leistd.Timing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 两步验证：设置与启用、登录第二步、恢复码、强制启用与管理员重置。
/// </summary>
/// <remarks>
/// 时钟换成手动推进的：同一步的验证码只认一次（防重放），用例要在不同的步上各取一个码，
/// 靠真实时钟就得等 30 秒或碰运气。
/// </remarks>
public sealed class TwoFactorTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "TwoFactorTests!Passw0rd";

    [Fact]
    public async Task Enabling_requires_a_code_and_signs_out_other_devices()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_enable");
        using var other = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        using var mine = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        var secret = await BeginSetupAsync(mine.Client);
        using (var wrong = await mine.Client.PostAsJsonAsync("/api/v1/auth/me/two-factor/enable", new { Code = "000000" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var codes = await EnableAsync(mine.Client, secret, clock);

        Assert.Equal(RecoveryCodes.Count, codes.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        var status = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me/two-factor");
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(RecoveryCodes.Count, status.GetProperty("recoveryCodesLeft").GetInt32());
    }

    [Fact]
    public async Task Sign_in_requires_the_second_step_and_rejects_a_reused_code()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_login");
        var secret = await EnableForAsync(host, username, clock);

        clock.Advance();
        var (first, token) = await PasswordStepAsync(host, username);
        Assert.False(first.Headers.Contains("Set-Cookie"));

        var code = Code(secret, clock);
        using var client = await SecondStepAsync(host, token, new { Token = token, Code = code });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        // 同一步的码重放：换一个新挑战也不认
        var (_, replayToken) = await PasswordStepAsync(host, username);
        Assert.Equal("Auth:TwoFactorCodeInvalid",
            await SecondStepErrorAsync(host, new { Token = replayToken, Code = code }));
    }

    [Fact]
    public async Task Recovery_code_replaces_a_code_only_once()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_recovery");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var secret = await BeginSetupAsync(session.Client);
        var recovery = (await EnableAsync(session.Client, secret, clock))[0];

        var (_, token) = await PasswordStepAsync(host, username);
        using var client = await SecondStepAsync(host, token, new { Token = token, RecoveryCode = recovery.ToUpperInvariant() });
        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me/two-factor");
        Assert.Equal(RecoveryCodes.Count - 1, status.GetProperty("recoveryCodesLeft").GetInt32());

        var (_, again) = await PasswordStepAsync(host, username);
        Assert.Equal("Auth:TwoFactorCodeInvalid",
            await SecondStepErrorAsync(host, new { Token = again, RecoveryCode = recovery }));
    }

    [Fact]
    public async Task Failed_second_step_counts_toward_lockout_across_challenges()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_lockout");
        await EnableForAsync(host, username, clock);

        string? last = null;
        for (var i = 0; i < SettingConstant.Security.DefaultLockoutMaxFailedAttempts; i++)
        {
            // 每次都换一个新挑战：单个挑战的尝试上限挡不住这种试法，得靠账号级的失败计数
            var (_, token) = await PasswordStepAsync(host, username);
            last = await SecondStepErrorAsync(host, new { Token = token, Code = "000000" });
        }

        Assert.Equal("Auth:UserTemporarilyLockedOut", last);
    }

    [Fact]
    public async Task Disabling_requires_password_and_code()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_disable");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var secret = await BeginSetupAsync(session.Client);
        await EnableAsync(session.Client, secret, clock);
        clock.Advance();

        using (var wrongPassword = await session.Client.PostAsJsonAsync("/api/v1/auth/me/two-factor/disable",
                   new { Password = Password + "x", Code = Code(secret, clock) }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        }

        using var disable = await session.Client.PostAsJsonAsync("/api/v1/auth/me/two-factor/disable",
            new { Password, Code = Code(secret, clock) });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        using var direct = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        Assert.Equal(HttpStatusCode.OK, (await direct.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Tenant_requirement_grants_a_restricted_session_until_enabled()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_required");
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        await WriteSettingAsync(admin.Client, SettingConstant.Security.RequireTwoFactor, "true");
        try
        {
            using var restricted = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

            var me = await restricted.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
            Assert.True(me.GetProperty("twoFactorSetupRequired").GetBoolean());
            using (var blocked = await restricted.Client.GetAsync("/api/v1/auth/me/sessions"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
                Assert.Equal("Auth:TwoFactorSetupRequired", await ErrorCodeAsync(blocked));
            }

            // 页面不拦：整页刷新任何地址都要能拿到前端壳，再由路由守卫带去设置页
            using (var page = await restricted.Client.GetAsync("/workspace"))
            {
                Assert.NotEqual(HttpStatusCode.Forbidden, page.StatusCode);
            }

            var secret = await BeginSetupAsync(restricted.Client);
            using var enable = await restricted.Client.PostAsJsonAsync(
                "/api/v1/auth/me/two-factor/enable", new { Code = Code(secret, clock) });
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

            // 换发的会话不再受限；旧的受限会话随之结束
            using var reissued = ProjectWebApplicationFactory.CreateProjectClient(host);
            reissued.DefaultRequestHeaders.Add("Cookie", CookieOf(enable));
            Assert.Equal(HttpStatusCode.OK, (await reissued.GetAsync("/api/v1/auth/me/sessions")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await restricted.Client.GetAsync("/api/v1/auth/me")).StatusCode);

            clock.Advance();
            using var disable = await reissued.PostAsJsonAsync("/api/v1/auth/me/two-factor/disable",
                new { Password, Code = Code(secret, clock) });
            Assert.Equal("Auth:TwoFactorRequiredByPolicy", await ErrorCodeAsync(disable));
        }
        finally
        {
            await WriteSettingAsync(admin.Client, SettingConstant.Security.RequireTwoFactor, null);
        }
    }

    [Fact]
    public async Task Admin_reset_removes_the_second_step()
    {
        var (host, clock) = CreateHost();
        using var _ = host;
        var username = await CreateUserAsync(host, "tfa_reset");
        await EnableForAsync(host, username, clock);

        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var users = await admin.Client.GetFromJsonAsync<JsonElement>($"/api/v1/users?offset=0&limit=10&keyword={username}");
        var user = users.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("username").GetString() == username);
        Assert.True(user.GetProperty("isTwoFactorEnabled").GetBoolean());

        using var reset = await admin.Client.PostAsync($"/api/v1/users/{user.GetProperty("id").GetGuid()}/reset-two-factor", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var direct = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        Assert.Equal(HttpStatusCode.OK, (await direct.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private (WebApplicationFactory<Program> Host, TestClock Clock) CreateHost()
    {
        var clock = new TestClock();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        }));
        return (host, clock);
    }

    private async Task<string> EnableForAsync(WebApplicationFactory<Program> host, string username, TestClock clock)
    {
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var secret = await BeginSetupAsync(session.Client);
        await EnableAsync(session.Client, secret, clock);
        return secret;
    }

    private static async Task<string> BeginSetupAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/v1/auth/me/two-factor/setup", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var secret = body.RootElement.GetProperty("secret").GetString()!;
        Assert.StartsWith("otpauth://totp/", body.RootElement.GetProperty("otpAuthUri").GetString());
        return secret;
    }

    private static async Task<List<string>> EnableAsync(HttpClient client, string secret, TestClock clock)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/me/two-factor/enable", new { Code = Code(secret, clock) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
    }

    private static async Task<(HttpResponseMessage Response, string Token)> PasswordStepAsync(
        WebApplicationFactory<Program> host, string username)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        return (response, body.RootElement.GetProperty("twoFactorToken").GetString()!);
    }

    private static async Task<HttpClient> SecondStepAsync(WebApplicationFactory<Program> host, string token, object input)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/two-factor", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", CookieOf(response));
        return client;
    }

    private static async Task<string?> SecondStepErrorAsync(WebApplicationFactory<Program> host, object input)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/two-factor", input);
        Assert.False(response.IsSuccessStatusCode);
        return await ErrorCodeAsync(response);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string CookieOf(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));

    private static string Code(string secret, TestClock clock) =>
        Totp.ComputeCode(Base32.Decode(secret)!, Totp.TimeStepAt(clock.Now));

    private static async Task<string> CreateUserAsync(WebApplicationFactory<Program> host, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
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

    private static async Task WriteSettingAsync(HttpClient client, string name, string? value)
    {
        using var response = await client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant", new { Name = name, Value = value });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>手动推进的时钟：每推一次跨过一个验证码步长。</summary>
    private sealed class TestClock : IClock
    {
        public DateTime Now { get; private set; } = new(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

        public void Advance() => Now = Now.AddSeconds(Totp.StepSeconds);

        public DateTime Normalize(DateTime dateTime) =>
            dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }
}
#endif
