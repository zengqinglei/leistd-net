#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CompanyName.ProjectName.Application.Auth.Passkeys;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
#if (IncludeMultiTenancy)
using Microsoft.Extensions.Configuration;
#endif
using CompanyName.ProjectName.Application.Settings.Provider;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 用通行密钥登录：免口令登录直接签发会话、满足组织要求；口令登录之后作为第二步的默认方式。
/// 断言经真实协议校验：仪式只认一次、来源与计数器不对不认，校验失败保留记录、不计入账号锁定，第二步挑战仍限次。
/// </summary>
public sealed class PasskeySignInTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "PasskeySignIn!Passw0rd";

    [Fact]
    public async Task A_passkey_signs_in_without_a_password_or_a_second_step()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_passwordless");

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        var (ceremony, options) = await BeginSignInAsync(client);
        Assert.Equal(0, options.GetProperty("allowCredentials").GetArrayLength());
        using var signedIn = await CompleteSignInAsync(client, ceremony, passkey.Sign(options));

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        AddCookies(client, signedIn);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(username, me.GetProperty("username").GetString());
    }

    [Fact]
    public async Task A_sign_in_ceremony_counts_once()
    {
        var (_, passkey) = await CreateUserWithPasskeyAsync("pks_once");
        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        var (ceremony, options) = await BeginSignInAsync(client);
        var assertion = passkey.Sign(options);
        using (await CompleteSignInAsync(client, ceremony, assertion))
        {
        }

        using var replay = await CompleteSignInAsync(client, ceremony, assertion);
        Assert.Equal("Auth:PasskeyCeremonyExpired", await ErrorCodeAsync(replay));
    }

    // 计数器始终为 0 的认证器（合规、常见于可同步的通行密钥）让同一断言两次都校验得过：只认一次只能靠仪式本身。
    // 第一次提交停在读出仪式状态那一刻，另一次同时提交同一份断言，必须挡在仪式锁上，而不是也读到同一份状态
    [Fact]
    public async Task Concurrent_completions_of_one_ceremony_sign_in_at_most_once()
    {
        var gate = new InterleavingGate();
        var waits = new LockWaitProbe();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            InterleavingGate.Wrap<IDistributedCache>(services, inner => new SignInCeremonyReadGate(gate, inner));
            waits.Install(services);
        }));
        var (_, passkey) = await CreateUserWithPasskeyAsync("pks_race", host);
        using var first = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var second = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (ceremony, options) = await BeginSignInAsync(first);
        var assertion = passkey.Sign(options, signCount: 0);

        gate.Arm();
        var firstCompletion = CompleteSignInAsync(first, ceremony, JsonNode.Parse(assertion.ToJsonString())!.AsObject());
        await gate.Entered;
        waits.Arm(PasskeyCeremonyStore.SignInLockKey(ceremony));
        var secondCompletion = CompleteSignInAsync(second, ceremony, JsonNode.Parse(assertion.ToJsonString())!.AsObject());
        await waits.AssertWaitingAsync(secondCompletion);
        gate.Release();

        using var firstResponse = await firstCompletion;
        using var secondResponse = await secondCompletion;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("Auth:PasskeyCeremonyExpired", await ErrorCodeAsync(secondResponse));
    }

    [Fact]
    public async Task An_unknown_credential_is_rejected()
    {
        using var stranger = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var (_, unregistered) = SoftwareAuthenticator.Create(await PasskeyRequests.BeginAsync(stranger.Client));

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        var (ceremony, options) = await BeginSignInAsync(client);
        using var rejected = await CompleteSignInAsync(client, ceremony, unregistered.Sign(options));

        Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(rejected));
    }

    [Fact]
    public async Task Failed_assertions_for_a_known_account_do_not_lock_out_password_sign_in()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_lockout");
        await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.LockoutMaxFailedAttempts, "2");
        try
        {
            using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
            // 先成功用一次：存下的计数器大于 0，之后的 0 才算回退
            var (first, options) = await BeginSignInAsync(client);
            using (var ok = await CompleteSignInAsync(client, first, passkey.Sign(options)))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            (first, options) = await BeginSignInAsync(client);
            using (var wrongOrigin = await CompleteSignInAsync(client, first, passkey.Sign(options, origin: "https://evil.example")))
            {
                Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(wrongOrigin));
            }

            // 计数器回退：签名本身对，计数器比登记时还小
            (first, options) = await BeginSignInAsync(client);
            using (var cloned = await CompleteSignInAsync(client, first, passkey.Sign(options, signCount: 0)))
            {
                Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(cloned));
            }

            using var password = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            Assert.True((await password.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresTwoFactor").GetBoolean());
            using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
            var userId = await FindUserIdAsync(factory, username);
            var failures = await OperationRecordQueries.GetFailuresAsync(factory, admin.Client, OperationRecordActions.AuthLoginFailed, userId.ToString());
            Assert.Equal(2, failures.Count);
            Assert.All(failures, failure => Assert.Equal("Auth:PasskeyVerificationFailed", failure.FailureCode));
        }
        finally
        {
            await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.LockoutMaxFailedAttempts, null);
        }
    }

    [Fact]
    public async Task After_a_password_the_passkey_is_the_default_second_step()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_second");

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        using var first = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
        var prompt = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(prompt.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal("passkey", prompt.GetProperty("defaultTwoFactorMethod").GetString());
        Assert.Equal(["passkey", "recoveryCode"], prompt.GetProperty("twoFactorMethods").EnumerateArray().Select(m => m.GetString()));
        var token = prompt.GetProperty("twoFactorToken").GetString()!;

        // 没取过选项就交来的断言不认，也不计数
        using (var early = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = new JsonObject { ["id"] = "x" } }))
        {
            Assert.Equal("Auth:PasskeyCeremonyExpired", await ErrorCodeAsync(early));
        }

        using var optionsResponse = await client.PostAsJsonAsync("/api/v1/auth/two-factor/passkey-options", new { token });
        var options = (await optionsResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("options");
        Assert.Equal(1, options.GetProperty("allowCredentials").GetArrayLength());

        using var signedIn = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = passkey.Sign(options) });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_passkey_alone_satisfies_a_two_factor_requirement()
    {
        var (_, passkey) = await CreateUserWithPasskeyAsync("pks_required");
        await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.RequireTwoFactor, "true");
        try
        {
            using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
            var (ceremony, options) = await BeginSignInAsync(client);
            using var signedIn = await CompleteSignInAsync(client, ceremony, passkey.Sign(options));
            AddCookies(client, signedIn);

            var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
            Assert.False(me.GetProperty("twoFactorSetupRequired").GetBoolean());
        }
        finally
        {
            await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.RequireTwoFactor, null);
        }
    }

#if (Email)
    [Fact]
    public async Task Email_recovery_does_not_block_passwordless_sign_in()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_recovered");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var user = await db.Set<User>().SingleAsync(u => u.Username == username);
            user.RecoverPasswordHash(user.PasswordHash!);
            await db.SaveChangesAsync();
        }

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        var (ceremony, options) = await BeginSignInAsync(client);
        using var signedIn = await CompleteSignInAsync(client, ceremony, passkey.Sign(options));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

#endif

    [Fact]
    public async Task A_failed_second_step_consumes_the_passkey_options_but_not_the_account_attempts()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_step_failed");
        await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.LockoutMaxFailedAttempts, "2");
        try
        {
            using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
            using var first = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
            var token = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorToken").GetString()!;
            for (var i = 0; i < 2; i++)
            {
                var options = await SecondStepOptionsAsync(client, token);
                using var wrong = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = passkey.Sign(options, origin: "https://evil.com") });
                Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(wrong));
                using var replay = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = passkey.Sign(options) });
                Assert.Equal("Auth:PasskeyCeremonyExpired", await ErrorCodeAsync(replay));
            }

            var fresh = await SecondStepOptionsAsync(client, token);
            using var ok = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = passkey.Sign(fresh) });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            using var password = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            using var admin = await ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
            var failures = await OperationRecordQueries.GetFailuresAsync(factory, admin.Client, OperationRecordActions.AuthLoginFailed, (await FindUserIdAsync(factory, username)).ToString());
            Assert.Equal(2, failures.Count);
        }
        finally
        {
            await ProjectWebApplicationFactory.SetTenantSettingAsync(factory, SettingConstant.Security.LockoutMaxFailedAttempts, null);
        }
    }

    [Fact]
    public async Task Failed_passkeys_still_exhaust_the_second_step_challenge()
    {
        var (username, passkey) = await CreateUserWithPasskeyAsync("pks_step_cap");
        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        using var first = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = username, Password });
        var token = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorToken").GetString()!;
        for (var i = 1; i <= 5; i++)
        {
            var options = await SecondStepOptionsAsync(client, token);
            using var wrong = await client.PostAsJsonAsync("/api/v1/auth/two-factor", new { token, passkey = passkey.Sign(options, origin: "https://evil.com") });
            Assert.Equal(i == 5 ? "Auth:TwoFactorChallengeExpired" : "Auth:PasskeyVerificationFailed", await ErrorCodeAsync(wrong));
        }
    }
#if (IncludeMultiTenancy)

    [Theory]
    [InlineData("https://acme.example.com", true)]
    [InlineData("https://other.example.com", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("https://example.com", true)]
    [InlineData("http://acme.example.com", false)]
    [InlineData("https://acme.example.com:8443", false)]
    public async Task Domain_origins_are_limited_to_the_current_tenant_and_configured_origins(string origin, bool accepted)
    {
        using var isolated = new ProjectWebApplicationFactory();
        using var host = isolated.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Leistd:MultiTenancy:DomainFormat"] = "{0}.example.com",
                ["Passkeys:RelyingPartyId"] = "example.com",
                ["Passkeys:Origins:0"] = "https://example.com"
            })));
        using var client = await CreateAcmeSessionAsync(host);
        client.DefaultRequestHeaders.Host = "acme.example.com";
        var options = await PasskeyRequests.BeginAsync(client);
        Assert.Equal("example.com", options.GetProperty("rp").GetProperty("id").GetString());
        var (registration, _) = SoftwareAuthenticator.Create(options);
        RewriteRegistrationOrigin(registration, origin);
        using (var result = await client.PostAsJsonAsync("/api/v1/auth/me/two-factor/passkeys", new { credential = registration, name = "Tenant passkey" }))
        {
            if (accepted)
                Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
            else
                Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(result));
        }
        // 使用固定的允许来源登记另一把，独立验证登录路径（失败登记也要覆盖登录来源拒绝）。
        options = await PasskeyRequests.BeginAsync(client);
        var (valid, passkey) = SoftwareAuthenticator.Create(options);
        RewriteRegistrationOrigin(valid, "https://example.com");
        using (var registered = await client.PostAsJsonAsync("/api/v1/auth/me/two-factor/passkeys", new { credential = valid, name = "Sign-in key" }))
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(host);
        anonymous.DefaultRequestHeaders.Host = "acme.example.com";
        var (ceremony, assertionOptions) = await BeginSignInAsync(anonymous);
        using var signedIn = await CompleteSignInAsync(anonymous, ceremony, passkey.Sign(assertionOptions, origin: origin));
        if (accepted)
            Assert.True(signedIn.IsSuccessStatusCode, await signedIn.Content.ReadAsStringAsync());
        else
            Assert.Equal("Auth:PasskeyVerificationFailed", await ErrorCodeAsync(signedIn));
    }

    [Fact]
    public async Task Registration_names_identify_the_tenant()
    {
        using var isolated = new ProjectWebApplicationFactory();
        using var client = await CreateAcmeSessionAsync(isolated);
        var options = await PasskeyRequests.BeginAsync(client);
        Assert.Equal("admin@acme", options.GetProperty("user").GetProperty("name").GetString());
        Assert.EndsWith("@acme", options.GetProperty("user").GetProperty("displayName").GetString());
    }

    private static async Task<HttpClient> CreateAcmeSessionAsync(WebApplicationFactory<Program> host)
    {
        using (var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword))
        using (var tenant = await admin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "acme", AdminEmail = "admin@acme.example.test", AdminPassword = Password
        }))
        {
            Assert.Equal(HttpStatusCode.OK, tenant.StatusCode);
        }

        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add("X-Tenant", "acme");
        using (var login = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = "admin", Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            AddCookies(client, login);
        }
        return client;
    }

    private static void RewriteRegistrationOrigin(JsonObject response, string origin)
    {
        var data = response["response"]!["clientDataJSON"]!.GetValue<string>();
        var json = JsonNode.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(data))!;
        json["origin"] = origin;
        response["response"]!["clientDataJSON"] = System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    }
#endif

    private static async Task<JsonElement> SecondStepOptionsAsync(HttpClient client, string token)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/two-factor/passkey-options", new { token });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("options");
    }

    private static async Task<Guid> FindUserIdAsync(WebApplicationFactory<Program> host, string username)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyProjectDbContext>().Set<User>()
            .Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
    }

    private async Task<(string Username, SoftwarePasskey Passkey)> CreateUserWithPasskeyAsync(
        string prefix, WebApplicationFactory<Program>? host = null)
    {
        host ??= factory;
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        using (var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword))
        using (var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = $"{username}@example.test",
            Password,
            IsActive = true,
            IsEmailVerified = true
        }))
        {
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        }

        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var (_, passkey) = await PasskeyRequests.RegisterAsync(session.Client);
        return (username, passkey);
    }

    // 拦免口令登录仪式的第一次读取：此刻持着仪式锁（若有），读到的状态还没删
    private sealed class SignInCeremonyReadGate(InterleavingGate gate, IDistributedCache inner) : IDistributedCache
    {
        public byte[]? Get(string key) => inner.Get(key);

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (key.Contains(":sign-in:", StringComparison.Ordinal))
                await gate.PassAsync();
            return await inner.GetAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            inner.SetAsync(key, value, options, token);

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }

    private static async Task<(string Ceremony, JsonElement Options)> BeginSignInAsync(HttpClient client)
    {
        using var begin = await client.PostAsJsonAsync("/api/v1/auth/passkey-login/options", new { });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var body = await begin.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("ceremony").GetString()!, body.GetProperty("options"));
    }

    private static Task<HttpResponseMessage> CompleteSignInAsync(HttpClient client, string ceremony, JsonObject credential) =>
        client.PostAsJsonAsync("/api/v1/auth/passkey-login", new { ceremony, credential });

    private static void AddCookies(HttpClient client, HttpResponseMessage response) =>
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
#endif
