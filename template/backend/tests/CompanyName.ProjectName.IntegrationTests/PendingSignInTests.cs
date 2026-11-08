#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
#if (OpenIddictServer)
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CompanyName.ProjectName.Application.Auth.Options;
using Microsoft.AspNetCore.WebUtilities;
#endif
using Leistd.Security.OneTimeCodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 待完成的登录（第一步已通过、尚待第二步）由官方两步登录方案的临时 Cookie 承载：HttpOnly、不持久、不滑动，
/// 绝对期限同挑战；它不是正式身份，刷新可续，完成、放弃、换一次登录或失效时都删掉。
/// </summary>
/// <remarks>
/// 时钟换成手动推进的：过期要让 Cookie 票据本身过期（处理器按同一个 TimeProvider 判定），不靠删服务端记录冒充。
/// </remarks>
public sealed class PendingSignInTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "PendingSignIn!Passw0rd";
    private const string ContextUrl = "/api/v1/auth/two-factor";

    // 待完成登录的期限：开启邮箱能力时是邮箱验证码有效期（默认 5 分钟）的两倍，否则 5 分钟
#if (Email)
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
#else
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
#endif

    /// <summary>第一步只写临时 Cookie：HttpOnly、不持久、SameSite=Lax，不写会话 Cookie，响应体里没有能完成登录的凭据。</summary>
    [Fact]
    public async Task The_first_step_writes_only_the_pending_sign_in_cookie()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_first");

        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = user.Username, Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(PendingSignIn.CookieName(host) + "=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        // 不持久：没有 Expires / Max-Age，浏览器关闭即丢
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", setCookie, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("twoFactorToken", out _));
        Assert.Equal(["authenticator", "recoveryCode"],
            body.RootElement.GetProperty("twoFactorMethods").EnumerateArray().Select(method => method.GetString()));
    }

    /// <summary>期限绝对、不滑动：临近到期的请求不续发 Cookie，到期即失效并删掉 Cookie。</summary>
    [Fact]
    public async Task The_pending_sign_in_has_an_absolute_lifetime()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_absolute");
        var pending = await PasswordStepAsync(host, user);

        clock.Advance(ChallengeLifetime - TimeSpan.FromMinutes(1));
        using (var client = PendingSignIn.Client(host, pending))
        using (var nearExpiry = await client.GetAsync(ContextUrl))
        {
            Assert.Equal(HttpStatusCode.OK, nearExpiry.StatusCode);
            Assert.Empty(PendingSignIn.SetCookies(host, nearExpiry));
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        using (var client = PendingSignIn.Client(host, pending))
        using (var expired = await client.GetAsync(ContextUrl))
        {
            Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(expired));
            Assert.True(PendingSignIn.Deletes(host, expired));
        }
    }

    /// <summary>临时身份不是正式身份：只带它调需要登录的接口一律 401。</summary>
    [Fact]
    public async Task The_pending_identity_is_not_a_signed_in_identity()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_anon");
        var pending = await PasswordStepAsync(host, user);

        using var client = PendingSignIn.Client(host, pending);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users?offset=0&limit=10")).StatusCode);
#if (OpenIddictServer)
        // userinfo 只认访问令牌：没有令牌时 OpenIddict 按协议答 invalid_request（400），临时 Cookie 换不来用户资料
        using (var userInfo = await client.GetAsync("/connect/userinfo"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, userInfo.StatusCode);
            Assert.DoesNotContain(user.Username, await userInfo.Content.ReadAsStringAsync());
        }
#endif
    }

    /// <summary>第二步页刷新后凭临时 Cookie 取回可用方式与默认方式，照常完成。</summary>
    [Fact]
    public async Task Refreshing_the_second_step_reads_the_same_context()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_refresh");
        var pending = await PasswordStepAsync(host, user);

        using var client = PendingSignIn.Client(host, pending);
        for (var load = 0; load < 2; load++)
        {
            var context = await client.GetFromJsonAsync<JsonElement>(ContextUrl);
            Assert.Equal("authenticator", context.GetProperty("defaultTwoFactorMethod").GetString());
            Assert.Equal(["authenticator", "recoveryCode"],
                context.GetProperty("twoFactorMethods").EnumerateArray().Select(method => method.GetString()));
        }

        Step(clock);
        using var signedIn = await client.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>没有待完成登录时取上下文按已过期拒绝，不写 Cookie。</summary>
    [Fact]
    public async Task Without_a_pending_sign_in_the_context_is_expired()
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        using var response = await client.GetAsync(ContextUrl);
        Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(response));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>第二步通过：响应删除临时 Cookie 并签发会话 Cookie；挑战一次消费，拿旧 Cookie 重放按已过期拒绝。</summary>
    [Fact]
    public async Task Completing_deletes_the_pending_cookie_and_the_challenge_is_consumed_once()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_done");
        var pending = await PasswordStepAsync(host, user);

        Step(clock);
        using var client = PendingSignIn.Client(host, pending);
        using var signedIn = await client.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.True(PendingSignIn.Deletes(host, signedIn));
        var setCookies = signedIn.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, value => value.StartsWith(ProjectWebApplicationFactory.SessionCookieName + "=", StringComparison.Ordinal));
        // 先删临时 Cookie，再写会话 Cookie
        Assert.True(setCookies.FindIndex(value => value.StartsWith(PendingSignIn.CookieName(host) + "=", StringComparison.Ordinal)) <
            setCookies.FindIndex(value => value.StartsWith(ProjectWebApplicationFactory.SessionCookieName + "=", StringComparison.Ordinal)));

        Step(clock);
        using var replay = PendingSignIn.Client(host, pending);
        using var replayed = await replay.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) });
        Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(replayed));
        Assert.True(PendingSignIn.Deletes(host, replayed));
    }

    /// <summary>已有待完成登录时再走一次第一步：先删旧的再写新的，旧挑战作废。</summary>
    [Fact]
    public async Task A_new_pending_sign_in_replaces_the_previous_one()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_replace");
        var previous = await PasswordStepAsync(host, user);

        using var browser = PendingSignIn.Client(host, previous);
        using var again = await browser.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = user.Username, Password });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var written = PendingSignIn.SetCookies(host, again);
        Assert.Equal(2, written.Count);
        Assert.EndsWith("=", written[0].Split(';', 2)[0]);
        var current = PendingSignIn.Read(host, again);
        Assert.NotNull(current);
        Assert.NotEqual(previous, current);

        using (var old = PendingSignIn.Client(host, previous))
        using (var stale = await old.GetAsync(ContextUrl))
        {
            Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(stale));
        }

        Step(clock);
        using var fresh = PendingSignIn.Client(host, current!);
        using var signedIn = await fresh.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>放弃（换账号、返回登录）作废挑战并删 Cookie；没有待完成登录时同样成功、什么也不写。</summary>
    [Fact]
    public async Task Cancelling_ends_the_pending_sign_in_and_is_idempotent()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_cancel");
        var pending = await PasswordStepAsync(host, user);

        using (var client = PendingSignIn.Client(host, pending))
        using (var cancelled = await client.PostAsync("/api/v1/auth/two-factor/cancel", null))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.True(PendingSignIn.Deletes(host, cancelled));
        }

        using (var client = PendingSignIn.Client(host, pending))
        using (var stale = await client.GetAsync(ContextUrl))
        {
            Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(stale));
        }

        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var nothing = await anonymous.PostAsync("/api/v1/auth/two-factor/cancel", null);
        Assert.Equal(HttpStatusCode.OK, nothing.StatusCode);
        Assert.False(nothing.Headers.Contains("Set-Cookie"));
    }

    /// <summary>票据已过期的临时 Cookie：口令登录、退出与取上下文都删掉它（官方处理器只返回失败、不删）。</summary>
    [Theory]
    [InlineData("password-login")]
    [InlineData("logout")]
    [InlineData("context")]
    public async Task An_expired_pending_cookie_is_deleted(string entry)
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_expired");
        var pending = await PasswordStepAsync(host, user);
        clock.Advance(ChallengeLifetime + TimeSpan.FromMinutes(1));

        using var browser = PendingSignIn.Client(host, pending);
        using var response = entry switch
        {
            // 单步登录的账号：签发会话前删掉过期的临时 Cookie
            "password-login" => await browser.PostAsJsonAsync("/api/v1/auth/session-login",
                new { UsernameOrEmail = "admin", Password = ProjectWebApplicationFactory.TestAdminPassword }),
            "logout" => await browser.PostAsync("/api/v1/auth/logout", null),
            _ => await browser.GetAsync(ContextUrl)
        };

        Assert.True(PendingSignIn.Deletes(host, response));
    }

    /// <summary>票据被篡改的临时 Cookie：取上下文、提交、发码、取通行密钥选项都按已过期拒绝并删掉它；取消照常成功并删掉它。</summary>
    [Theory]
    [InlineData("context")]
    [InlineData("submit")]
#if (Email)
    [InlineData("email-code")]
#endif
    [InlineData("passkey-options")]
    [InlineData("cancel")]
    public async Task A_tampered_pending_cookie_is_rejected_and_deleted(string entry)
    {
        var (host, _) = CreateHost();
        using var ownedHost = host;
        using var client = PendingSignIn.Client(host, PendingSignIn.CookieName(host) + "=CfDJ8tampered");

        using var response = entry switch
        {
            "context" => await client.GetAsync(ContextUrl),
            "submit" => await client.PostAsJsonAsync(ContextUrl, new { Code = "123456" }),
            "email-code" => await client.PostAsync("/api/v1/auth/two-factor/email-code", null),
            "passkey-options" => await client.PostAsync("/api/v1/auth/two-factor/passkey-options", null),
            _ => await client.PostAsync("/api/v1/auth/two-factor/cancel", null)
        };

        if (entry == "cancel")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(response));
        }

        Assert.True(PendingSignIn.Deletes(host, response));
    }

    /// <summary>同一个待完成登录上同时交两种有效方式：凭据锁串行、挑战一次消费，只登记一个会话。</summary>
    [Fact]
    public async Task Two_methods_submitted_together_register_a_single_session()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_concurrent");
        var pending = await PasswordStepAsync(host, user);

        Step(clock);
        using var first = PendingSignIn.Client(host, pending);
        using var second = PendingSignIn.Client(host, pending);
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) }),
            second.PostAsJsonAsync(ContextUrl, new { RecoveryCode = user.RecoveryCodes[0] }));
        try
        {
            var signedIn = Assert.Single(responses, response => response.IsSuccessStatusCode);
            Assert.Equal("Auth:TwoFactorChallengeExpired",
                await ErrorCodeAsync(Assert.Single(responses, response => !response.IsSuccessStatusCode)));

            using var session = ProjectWebApplicationFactory.CreateProjectClient(host);
            session.DefaultRequestHeaders.Add("Cookie", SessionCookieOf(signedIn));
            var sessions = await session.GetFromJsonAsync<JsonElement>("/api/v1/auth/me/sessions");
            Assert.Equal(1, sessions.GetArrayLength());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    /// <summary>Cookie 仍有效、服务端挑战却已失效（凭据变了）：取上下文按已过期拒绝并删 Cookie。</summary>
    [Fact]
    public async Task A_pending_sign_in_voided_by_a_credential_change_is_deleted()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_stamp");
        var pending = await PasswordStepAsync(host, user);

        using (var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword))
        using (var reset = await admin.Client.PostAsJsonAsync($"/api/v1/users/{user.Id}/reset-password", new { Password = Password + "2" }))
        {
            Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        }

        using var client = PendingSignIn.Client(host, pending);
        using var stale = await client.GetAsync(ContextUrl);
        Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(stale));
        Assert.True(PendingSignIn.Deletes(host, stale));
    }
#if (OpenIddictServer)

    /// <summary>
    /// 两个依赖方各自发起交互授权：都回到第二步页、各带自己的缓存请求；完成一次第二步后两者分别续跑拿到授权码。
    /// </summary>
    [Fact]
    public async Task Two_relying_parties_resume_the_same_pending_sign_in_and_each_get_a_code()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_two_rp");
        var (first, second) = await CreatePublicClientsAsync(host);
        var pending = await PasswordStepAsync(host, user);
        using var browser = Browser(host, pending);

        var firstReturn = await SecondStepReturnUrlAsync(browser, AuthorizationUrl(first, "&state=first"));
        var secondReturn = await SecondStepReturnUrlAsync(browser, AuthorizationUrl(second, "&state=second"));
        Assert.Equal(first, QueryHelpers.ParseQuery(new Uri(browser.BaseAddress!, firstReturn).Query)["client_id"].ToString());
        Assert.Equal(second, QueryHelpers.ParseQuery(new Uri(browser.BaseAddress!, secondReturn).Query)["client_id"].ToString());
        Assert.NotEqual(RequestUriOf(browser, firstReturn), RequestUriOf(browser, secondReturn));

        Step(clock);
        using (var signedIn = await browser.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) }))
        {
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            SetCookie(browser, SessionCookieOf(signedIn));
        }

        foreach (var (returnUrl, state) in new[] { (firstReturn, "first"), (secondReturn, "second") })
        {
            using var resumed = await browser.GetAsync(returnUrl);
            Assert.Equal(HttpStatusCode.Found, resumed.StatusCode);
            Assert.StartsWith(Callback, resumed.Headers.Location!.AbsoluteUri);
            var query = QueryHelpers.ParseQuery(resumed.Headers.Location.Query);
            Assert.True(query.ContainsKey("code"));
            Assert.Equal(state, query["state"].ToString());
        }
    }

    /// <summary>静默授权：待完成的登录还不是认证完成，按协议回 <c>login_required</c>，不跳页、没有授权码。</summary>
    [Fact]
    public async Task A_silent_authorization_with_a_pending_sign_in_is_login_required()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_silent");
        var (clientId, _) = await CreatePublicClientsAsync(host);
        var pending = await PasswordStepAsync(host, user);
        using var browser = Browser(host, pending);

        using var silent = await browser.GetCachedAsync(AuthorizationUrl(clientId, "&prompt=none&state=probe"));
        Assert.Equal(HttpStatusCode.Found, silent.StatusCode);
        Assert.StartsWith(Callback, silent.Headers.Location!.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(silent.Headers.Location.Query);
        Assert.Equal("login_required", query["error"].ToString());
        Assert.False(query.ContainsKey("code"));
    }

    /// <summary>
    /// 要求重新认证（<c>prompt=login</c>、<c>max_age=0</c>、已超过的正数 <c>max_age</c>）时，更早的待完成登录不能抵消它：
    /// 回登录页、删掉旧临时 Cookie；重新输口令之后的待完成登录晚于证明，接回第二步，完成后同一个缓存请求签发授权码，
    /// 全程带着原证明。证明挪给另一个缓存请求不算数。
    /// </summary>
    [Theory]
    [InlineData("&prompt=login")]
    [InlineData("&max_age=0")]
    [InlineData("&max_age=60")]
    public async Task Reauthentication_resumes_only_with_a_pending_sign_in_started_after_the_proof(string demand)
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_reauth");
        var (clientId, _) = await CreatePublicClientsAsync(host);
        string? staleSession = null;
        if (demand == "&max_age=60")
        {
            // 正数 max_age：已有会话，但认证已是两分钟前
            Step(clock);
            using var signedIn = await PendingSignIn.Client(host, await PasswordStepAsync(host, user))
                .PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) });
            staleSession = SessionCookieOf(signedIn);
            clock.Advance(TimeSpan.FromMinutes(2));
        }

        var earlier = await PasswordStepAsync(host, user);
        using var browser = Browser(host, Cookies(staleSession, earlier));

        string loginReturn;
        using (var demanded = await browser.GetCachedAsync(AuthorizationUrl(clientId, demand + "&state=again")))
        {
            Assert.Equal(HttpStatusCode.Found, demanded.StatusCode);
            var location = new Uri(browser.BaseAddress!, demanded.Headers.Location!);
            Assert.Equal("/auth/login", location.AbsolutePath);
            var query = QueryHelpers.ParseQuery(location.Query);
            Assert.Equal("true", query["reauthenticate"].ToString());
            Assert.True(PendingSignIn.Deletes(host, demanded));
            loginReturn = query["returnUrl"].ToString();
            Assert.False(string.IsNullOrEmpty(ProofOf(browser, loginReturn)));
        }

        // 证明签发之后重新输口令：新的待完成登录接回第二步，回跳仍是同一个缓存请求、带着同一份证明
        clock.Advance(TimeSpan.FromSeconds(1));
        var later = await PasswordStepAsync(host, user);
        SetCookie(browser, Cookies(staleSession, later));
        using (var resumed = await browser.GetAsync(loginReturn))
        {
            Assert.Equal(HttpStatusCode.Found, resumed.StatusCode);
            var location = new Uri(browser.BaseAddress!, resumed.Headers.Location!);
            Assert.Equal("/auth/two-factor", location.AbsolutePath);
            Assert.Equal(loginReturn, QueryHelpers.ParseQuery(location.Query)["returnUrl"].ToString());
        }

        Step(clock);
        using (var signedIn = await browser.PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) }))
        {
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            SetCookie(browser, SessionCookieOf(signedIn));
        }

        using (var authorized = await browser.GetAsync(loginReturn))
        {
            Assert.Equal(HttpStatusCode.Found, authorized.StatusCode);
            Assert.StartsWith(Callback, authorized.Headers.Location!.AbsoluteUri);
            var query = QueryHelpers.ParseQuery(authorized.Headers.Location.Query);
            Assert.True(query.ContainsKey("code"));
            Assert.Equal("again", query["state"].ToString());
        }

        // 同一份证明挪给另一个缓存请求：新的待完成登录也接不回第二步，回登录页
        using var stranger = Browser(host, string.Empty);
        string otherReturn;
        using (var other = await stranger.GetCachedAsync(AuthorizationUrl(clientId, demand + "&state=other")))
        {
            otherReturn = QueryHelpers.ParseQuery(new Uri(stranger.BaseAddress!, other.Headers.Location!).Query)["returnUrl"].ToString();
        }

        var stolen = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["request_uri"] = RequestUriOf(stranger, otherReturn),
                ["reauthentication"] = ProofOf(browser, loginReturn)
            });
        clock.Advance(TimeSpan.FromSeconds(1));
        SetCookie(stranger, await PasswordStepAsync(host, user));
        using var misused = await stranger.GetAsync(stolen);
        Assert.Equal(HttpStatusCode.Found, misused.StatusCode);
        Assert.Equal("/auth/login", new Uri(stranger.BaseAddress!, misused.Headers.Location!).AbsolutePath);
    }

    /// <summary>有效的正式会话旁边还留着一个旧临时 Cookie：按正式会话签发授权码，旧临时 Cookie 删掉。</summary>
    [Fact]
    public async Task A_signed_in_session_wins_over_a_leftover_pending_cookie()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_both");
        var (clientId, _) = await CreatePublicClientsAsync(host);
        Step(clock);
        string session;
        using (var signedIn = await PendingSignIn.Client(host, await PasswordStepAsync(host, user))
            .PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) }))
        {
            session = SessionCookieOf(signedIn);
        }

        var leftover = await PasswordStepAsync(host, user);
        using var browser = Browser(host, Cookies(session, leftover));
        using var authorized = await browser.GetCachedAsync(AuthorizationUrl(clientId, "&state=both"));

        Assert.Equal(HttpStatusCode.Found, authorized.StatusCode);
        Assert.StartsWith(Callback, authorized.Headers.Location!.AbsoluteUri);
        Assert.True(QueryHelpers.ParseQuery(authorized.Headers.Location.Query).ContainsKey("code"));
        Assert.True(PendingSignIn.Deletes(host, authorized));
    }

    /// <summary>临时 Cookie 票据已过期：授权端点删掉它、回登录页，下一次进入不再看到它（不循环）。</summary>
    [Fact]
    public async Task An_expired_pending_cookie_sends_authorization_back_to_sign_in_once()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_oidc_exp");
        var (clientId, _) = await CreatePublicClientsAsync(host);
        var pending = await PasswordStepAsync(host, user);
        clock.Advance(ChallengeLifetime + TimeSpan.FromMinutes(1));
        using var browser = Browser(host, pending);

        using var interactive = await browser.GetCachedAsync(AuthorizationUrl(clientId, "&state=expired"));
        Assert.Equal(HttpStatusCode.Found, interactive.StatusCode);
        Assert.Equal("/auth/login", new Uri(browser.BaseAddress!, interactive.Headers.Location!).AbsolutePath);
        Assert.True(PendingSignIn.Deletes(host, interactive));
    }

    /// <summary>
    /// 只有临时身份时完成协议退出：待完成登录随之结束，临时 Cookie 删掉，旧挑战接不回第二步；票据已过期的同样删掉。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protocol_logout_ends_a_pending_sign_in_without_a_session(bool expired)
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_logout");
        var clientId = await CreateLogoutClientAsync(host);
        var pending = await PasswordStepAsync(host, user);
        if (expired)
            clock.Advance(ChallengeLifetime + TimeSpan.FromMinutes(1));
        using var browser = Browser(host, pending);

        using var signedOut = await browser.GetCachedAsync(
            $"/connect/logout?client_id={clientId}&post_logout_redirect_uri={Uri.EscapeDataString(SignedOut)}&state=bye");

        Assert.Equal(HttpStatusCode.Found, signedOut.StatusCode);
        Assert.StartsWith(SignedOut, signedOut.Headers.Location!.AbsoluteUri);
        Assert.True(PendingSignIn.Deletes(host, signedOut));
        using var stale = await PendingSignIn.Client(host, pending).GetAsync(ContextUrl);
        Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(stale));
    }

    /// <summary>正式会话旁还有一个待完成登录：协议退出结束会话时一并结束它，旧挑战接不回第二步。</summary>
    [Fact]
    public async Task Protocol_logout_of_a_session_also_ends_the_pending_sign_in_beside_it()
    {
        var (host, clock) = CreateHost();
        using var ownedHost = host;
        var user = await CreateTwoFactorUserAsync(host, clock, "pending_logout_both");
        var clientId = await CreateLogoutClientAsync(host);
        Step(clock);
        string session;
        using (var signedIn = await PendingSignIn.Client(host, await PasswordStepAsync(host, user))
            .PostAsJsonAsync(ContextUrl, new { Code = Code(user.Secret, clock) }))
        {
            session = SessionCookieOf(signedIn);
        }

        var idToken = await IdentityTokenAsync(host, clientId, session);
        var pending = await PasswordStepAsync(host, user);

        // hint 指向当前会话：直接退出（依赖方的跨站 POST 不带 Cookie，缓存后的顶层 GET 重入才带上）
        using var anonymous = Browser(host, string.Empty);
        using var cached = await anonymous.PostAsync("/connect/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_token_hint"] = idToken, ["post_logout_redirect_uri"] = SignedOut, ["state"] = "bye"
        }));
        var reentry = OidcRequestCaching.CachedLocation(anonymous, cached, "/connect/logout");
        using var browser = Browser(host, Cookies(session, pending));
        using var signedOut = await browser.GetAsync(reentry);

        Assert.StartsWith(SignedOut, signedOut.Headers.Location!.AbsoluteUri);
        Assert.True(PendingSignIn.Deletes(host, signedOut));
        using var me = ProjectWebApplicationFactory.CreateProjectClient(host);
        me.DefaultRequestHeaders.Add("Cookie", session);
        Assert.Equal(HttpStatusCode.Unauthorized, (await me.GetAsync("/api/v1/auth/me")).StatusCode);
        using var stale = await PendingSignIn.Client(host, pending).GetAsync(ContextUrl);
        Assert.Equal("Auth:TwoFactorChallengeExpired", await ErrorCodeAsync(stale));
    }

    private const string SignedOut = "https://localhost/oidc-signed-out";

    // 会话绑定的客户端：id_token 带会话标识，退出时 hint 才对得上当前会话
    private static async Task<string> CreateLogoutClientAsync(WebApplicationFactory<Program> host)
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var id = $"pending-out-{Guid.NewGuid():N}";
        using var response = await admin.Client.PostAsJsonAsync("/api/v1/open-applications", new
        {
            sessionBound = true,
            clientId = id,
            applicationType = "web",
            clientType = "public",
            redirectUris = new[] { Callback },
            postLogoutRedirectUris = new[] { SignedOut },
            permissions = new[] { "ept:authorization", "ept:token", "ept:end_session", "gt:authorization_code", "rst:code", "scp:openid", $"scp:{new OAuthResourceOptions().Resource}" },
            requirements = new[] { "ft:pkce" }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    // 用会话走一遍授权码流程，换出 id_token（PKCE 校验码与 AuthorizationUrl 的挑战对应）
    private static async Task<string> IdentityTokenAsync(WebApplicationFactory<Program> host, string clientId, string session)
    {
        using var browser = Browser(host, session);
        using var authorized = await browser.GetCachedAsync(AuthorizationUrl(clientId, "&state=token"));
        Assert.StartsWith(Callback, authorized.Headers.Location!.AbsoluteUri);
        using var exchange = Browser(host, string.Empty);
        using var token = await exchange.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["redirect_uri"] = Callback,
            ["code"] = QueryHelpers.ParseQuery(authorized.Headers.Location.Query)["code"].ToString(),
            ["code_verifier"] = new string('x', 64)
        }));
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());
        return (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id_token").GetString()!;
    }

    private const string Callback = "https://localhost/oidc-test";

    private static async Task<(string First, string Second)> CreatePublicClientsAsync(WebApplicationFactory<Program> host)
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        return (await CreatePublicClientAsync(admin.Client), await CreatePublicClientAsync(admin.Client));
    }

    private static async Task<string> CreatePublicClientAsync(HttpClient admin)
    {
        var id = $"pending-{Guid.NewGuid():N}";
        using var response = await admin.PostAsJsonAsync("/api/v1/open-applications", new
        {
            sessionBound = false,
            clientId = id,
            applicationType = "web",
            clientType = "public",
            redirectUris = new[] { Callback },
            permissions = new[] { "ept:authorization", "ept:token", "gt:authorization_code", "rst:code", "scp:openid", $"scp:{new OAuthResourceOptions().Resource}" },
            requirements = new[] { "ft:pkce" }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    // 交互授权遇到待完成登录：转到第二步页，返回它的 returnUrl（同一个缓存请求）
    private static async Task<string> SecondStepReturnUrlAsync(HttpClient browser, string authorizationUrl)
    {
        using var interactive = await browser.GetCachedAsync(authorizationUrl);
        Assert.Equal(HttpStatusCode.Found, interactive.StatusCode);
        var location = new Uri(browser.BaseAddress!, interactive.Headers.Location!);
        Assert.Equal("/auth/two-factor", location.AbsolutePath);
        var returnUrl = QueryHelpers.ParseQuery(location.Query)["returnUrl"].ToString();
        var target = new Uri(browser.BaseAddress!, returnUrl);
        Assert.Equal("/connect/authorize", target.AbsolutePath);
        Assert.Equal(["client_id", "request_uri"], QueryHelpers.ParseQuery(target.Query).Keys.Order());
        return returnUrl;
    }

    private static string RequestUriOf(HttpClient browser, string returnUrl) =>
        QueryHelpers.ParseQuery(new Uri(browser.BaseAddress!, returnUrl).Query)["request_uri"].ToString();

    private static string ProofOf(HttpClient browser, string returnUrl) =>
        QueryHelpers.ParseQuery(new Uri(browser.BaseAddress!, returnUrl).Query)["reauthentication"].ToString();

    private static string Cookies(params string?[] cookies) =>
        string.Join("; ", cookies.Where(cookie => !string.IsNullOrEmpty(cookie)));

    private static void SetCookie(HttpClient browser, string cookie)
    {
        browser.DefaultRequestHeaders.Remove("Cookie");
        browser.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    private static HttpClient Browser(WebApplicationFactory<Program> host, string cookie)
    {
        // 授权端点只收 HTTPS：TestServer 不做真实 TLS，改基地址即可
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.BaseAddress = new Uri("https://localhost");
        if (!string.IsNullOrEmpty(cookie))
            client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static string AuthorizationUrl(string client, string parameters) =>
        $"/connect/authorize?client_id={client}&redirect_uri={Uri.EscapeDataString(Callback)}&response_type=code" +
        $"&scope=openid%20{new OAuthResourceOptions().Resource}" +
        $"&code_challenge={Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(new string('x', 64))))}" +
        $"&code_challenge_method=S256{parameters}";
#endif

    private static string SessionCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(ProjectWebApplicationFactory.SessionCookieName + "=", StringComparison.Ordinal))
            .Split(';', 2)[0];

    private (WebApplicationFactory<Program> Host, FakeTimeProvider Clock) CreateHost()
    {
        // 从此刻起步：缓存条目按真实时钟回收，假时钟落在过去会让刚写入的挑战被缓存当成已过期。
        // 替换的是 TimeProvider，应用时钟（IClock）与 Cookie、OpenIddict 一并跟随
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        return (host, clock);
    }

    // 建一个启用了身份验证器的用户
    private static async Task<TestUser> CreateTwoFactorUserAsync(WebApplicationFactory<Program> host, FakeTimeProvider clock, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        Guid id;
        using (var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword))
        using (var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = $"{username}@example.test",
            Password,
            IsActive = true
        }))
        {
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        using var setup = await session.Client.PostAsJsonAsync("/api/v1/auth/me/two-factor/setup", new { });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        var secret = body.GetProperty("secret").GetString()!;
        using var enable = await session.Client.PostAsJsonAsync("/api/v1/auth/me/two-factor/enable",
            new { SetupId = body.GetProperty("setupId").GetGuid(), Code = Code(secret, clock) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var recoveryCodes = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")
            .EnumerateArray().Select(code => code.GetString()!).ToList();
        // 设置用的会话退出：后面数会话时只数第二步登记的
        using (await session.Client.PostAsync("/api/v1/auth/logout", null))
        {
        }

        return new TestUser(id, username, secret, recoveryCodes);
    }

    private static async Task<string> PasswordStepAsync(WebApplicationFactory<Program> host, TestUser user)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = user.Username, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return PendingSignIn.Read(host, response) ?? throw new InvalidOperationException("The first step wrote no pending sign-in cookie.");
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Code(string secret, FakeTimeProvider clock) =>
        Totp.ComputeCode(Totp.ParseSecret(secret)!, Totp.TimeStepAt(clock.GetUtcNow()));

    /// <summary>推进一个验证码步长。</summary>
    private static void Step(FakeTimeProvider clock) => clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

    private sealed record TestUser(Guid Id, string Username, string Secret, IReadOnlyList<string> RecoveryCodes);
}
#endif
