#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 再认证失败与登录共用同一套失败计数与锁定。
/// </summary>
/// <remarks>
/// <para>改口令要再证明一次自己知道当前口令，这是<b>再认证</b>，不是登录。曾经这条路径上
/// 既不计数也不查锁定：持有被盗会话的人能在改口令接口上<b>无限次</b>猜当前口令，
/// 把登录页那条"连续失败 5 次锁定 15 分钟"整个绕过去，而且不留任何审计。</para>
/// <para>光共用计数还不够，<b>必须在校验前先查锁定</b>：临时锁定按设计只挡新登录、不踢已有会话
/// （见 <c>User.AllowsExistingSessions</c>），只计数不拦的话攻击者锁了账号、自己手里
/// 那个会话照常用、可以接着猜——只是把本人挡在登录页外，比不改更糟。这组用例把两半都钉住。</para>
/// </remarks>
public sealed class ReauthenticationLockoutTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "ReauthTests!Passw0rd";
    private const string WrongPassword = "ReauthTests!Wrong000";
    private const string NewPassword = "ReauthTests!NewPassw0rd";
    private static readonly int Threshold = SettingConstant.Security.DefaultLockoutMaxFailedAttempts;

    /// <summary>猜错当前口令会计数，到阈值即锁定——而不是无限次可试。</summary>
    [Fact]
    public async Task Wrong_current_password_counts_toward_the_lockout_threshold()
    {
        var username = await CreateUserAsync("reauth_count");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        for (var i = 1; i < Threshold; i++)
        {
            Assert.Equal("Security:CurrentPasswordIncorrect", await ChangePasswordErrorAsync(session.Client, WrongPassword));
        }

        // 触发锁定的那一次改说"已锁定"，不再是"口令不正确"
        Assert.Equal("Auth:UserTemporarilyLockedOut", await ChangePasswordErrorAsync(session.Client, WrongPassword));
    }

    /// <summary>锁定期内不再校验口令：正确的当前口令也一样被拒，试不出哪个是对的。</summary>
    [Fact]
    public async Task The_correct_current_password_is_refused_while_locked_out()
    {
        var username = await CreateUserAsync("reauth_locked");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        for (var i = 0; i < Threshold; i++)
        {
            await ChangePasswordErrorAsync(session.Client, WrongPassword);
        }

        Assert.Equal("Auth:UserTemporarilyLockedOut", await ChangePasswordErrorAsync(session.Client, Password));
    }

    /// <summary>在改口令接口上把自己锁了，登录页也进不去——两边是同一个计数器。</summary>
    [Fact]
    public async Task A_lockout_earned_here_also_blocks_a_fresh_sign_in()
    {
        var username = await CreateUserAsync("reauth_shared");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        for (var i = 0; i < Threshold; i++)
        {
            await ChangePasswordErrorAsync(session.Client, WrongPassword);
        }

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = username, Password });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("Auth:UserTemporarilyLockedOut", await ErrorCodeAsync(login));
    }

    /// <summary>失败留下审计：以前这条路径上猜多少次都查不出来。</summary>
    [Fact]
    public async Task A_failed_attempt_is_recorded_for_the_audit_trail()
    {
        var username = await CreateUserAsync("reauth_audit");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        await ChangePasswordErrorAsync(session.Client, WrongPassword);

        using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var body = JsonDocument.Parse(await admin.Client.GetStringAsync(
            "/api/v1/operation-records?offset=0&limit=50&actions=auth.password.changed&outcome=Failed"));

        var failureCodes = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("failureCode").GetString())
            .ToList();
        Assert.Contains("Security:CurrentPasswordIncorrect", failureCodes);
    }

    /// <summary>锁定不踢已在线的本人：这是既有设计，改动不得把它带坏。</summary>
    [Fact]
    public async Task The_current_session_keeps_working_while_locked_out()
    {
        var username = await CreateUserAsync("reauth_session");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        for (var i = 0; i < Threshold; i++)
        {
            await ChangePasswordErrorAsync(session.Client, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    /// <summary>口令正确时照常改成功，计数不残留。</summary>
    [Fact]
    public async Task A_successful_change_still_works_after_some_failures()
    {
        var username = await CreateUserAsync("reauth_success");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, username, Password);

        await ChangePasswordErrorAsync(session.Client, WrongPassword);

        using var changed = await session.Client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { CurrentPassword = Password, NewPassword, ConfirmPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var signedIn = await ProjectWebApplicationFactory.LoginAsync(factory, username, NewPassword);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private static async Task<string?> ChangePasswordErrorAsync(HttpClient client, string currentPassword)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { CurrentPassword = currentPassword, NewPassword, ConfirmPassword = NewPassword });
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        return await ErrorCodeAsync(response);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
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
}
#endif
