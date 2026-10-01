#if (ExternalLogin)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.UnitOfWork;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 外部账号绑定：已登录用户绑定 / 解绑外部账号，绑定与登录的授权凭据互不通用。
/// </summary>
public sealed class ExternalLoginLinkTests
{
    private const string StateCookieName = "__Host-CompanyName.ProjectName.ExternalAuth.State";
    private const string Password = "LinkTests!Passw0rd";

    [Fact]
    public async Task Linked_external_account_signs_in_to_the_same_user()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_ok");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        provider.User = External("gh-link-ok");
        using (var link = await LinkAsync(host, session))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        var links = await ReadLinksAsync(session.Client);
        var github = links.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("provider").GetString() == "github");
        Assert.Equal("gh-link-ok", github.GetProperty("link").GetProperty("providerAccountLabel").GetString());

        // 用这个外部账号登录，进来的是绑定它的那个用户，而不是新建一个
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var signedIn = ProjectWebApplicationFactory.CreateProjectClient(host);
        signedIn.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));
        var me = await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(username, me.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Sign_in_and_link_authorizations_are_not_interchangeable()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-cross") };
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_cross");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        // 登录 state → 绑定端点
        var (loginState, loginCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/login-url");
        using (var misuse = await PostWithCookiesAsync(host, $"{session.Cookie}; {loginCookie}",
                   "/api/v1/external-auth/github/link", new { Code = "code", State = loginState }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, misuse.StatusCode);
        }

        // 绑定 state → 登录回调
        var (linkState, linkCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/link-url");
        using (var misuse = await PostWithCookiesAsync(host, linkCookie,
                   "/api/v1/external-auth/github/callback", new { Code = "code", State = linkState }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, misuse.StatusCode);
        }
    }

    [Fact]
    public async Task Rejects_an_external_account_linked_to_another_user()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-taken") };
        using var host = CreateHost(factory, provider);

        using var first = await ProjectWebApplicationFactory.LoginAsync(host, await CreateUserAsync(host, "link_first"), Password);
        using (var link = await LinkAsync(host, first))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        using var second = await ProjectWebApplicationFactory.LoginAsync(host, await CreateUserAsync(host, "link_second"), Password);
        using var taken = await LinkAsync(host, second);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("ExternalAuth:AlreadyLinked", await ErrorCodeAsync(taken));
    }

    [Fact]
    public async Task Last_external_login_can_be_unlinked_only_with_a_password()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-only") };
        using var host = CreateHost(factory, provider);

        // 经外部登录建出来的账号没有密码
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
        using var externalOnly = ProjectWebApplicationFactory.CreateProjectClient(host);
        externalOnly.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));

        var links = await ReadLinksAsync(externalOnly);
        Assert.False(links.GetProperty("hasPassword").GetBoolean());
        var linkId = LinkIdOf(links);
        using (var rejected = await externalOnly.DeleteAsync($"/api/v1/external-auth/links/{linkId}"))
        {
            Assert.Equal("ExternalAuth:LastSignInMethod", await ErrorCodeAsync(rejected));
        }

        // 设有密码的用户随时可以解绑
        provider.User = External("gh-with-password");
        var username = await CreateUserAsync(host, "link_unlink");
        using var withPassword = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        using (var link = await LinkAsync(host, withPassword))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        var stampBefore = await ReadSecurityStampAsync(host, username);
        using var unlink = await withPassword.Client.DeleteAsync($"/api/v1/external-auth/links/{LinkIdOf(await ReadLinksAsync(withPassword.Client))}");
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        Assert.Equal(JsonValueKind.Undefined, GithubLink(await ReadLinksAsync(withPassword.Client)).ValueKind);
        // 解绑是凭据变化：安全版本随之轮换并落库，凭这个外部账号完成第一步、尚未完成的登录挑战随之作废
        Assert.NotEqual(stampBefore, await ReadSecurityStampAsync(host, username));
    }

    /// <summary>
    /// 外部登录按邮箱关联已有账号，要求提供商确认邮箱已验证、且本地账号邮箱也已确认；
    /// 任一边没验证而邮箱已被占用时拒绝，不建新号、不留绑定，提示先登录再绑定。
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task External_sign_in_links_by_email_only_when_both_sides_are_verified(bool providerVerified, bool localConfirmed)
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_email");
        if (localConfirmed)
        {
            using var scope = host.Services.CreateScope();
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
            var user = await users.GetOneAsync(u => u.Username == username);
            user!.ConfirmEmail();
            await users.UpdateAsync(user);
            await unitOfWork.CompleteAsync();
        }

        provider.User = External("gh-by-email") with
        {
            Email = $"{username}@example.test",
            EmailVerified = providerVerified
        };
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });

        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var link = GithubLink(await ReadLinksAsync(session.Client));
        if (providerVerified && localConfirmed)
        {
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            Assert.Equal("gh-by-email", link.GetProperty("providerAccountLabel").GetString());
            return;
        }

        Assert.Equal(HttpStatusCode.Conflict, callback.StatusCode);
        Assert.Equal("ExternalAuth:AccountExistsSignInToLink", await ErrorCodeAsync(callback));
        Assert.Equal(JsonValueKind.Undefined, link.ValueKind);
    }

    /// <summary>
    /// 未验证的外部邮箱不写进新账号：否则就占用了别人的地址，本人随后注册会被挡、找回时接手的是对方建的号。
    /// </summary>
    [Fact]
    public async Task Unverified_external_email_is_not_taken_by_a_new_account()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        const string victimEmail = "victim@example.test";

        provider.User = External("gh-squatter") with { Email = victimEmail, EmailVerified = false };
        using var callback = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var signedIn = ProjectWebApplicationFactory.CreateProjectClient(host);
        signedIn.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));
        var me = await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.NotEqual(victimEmail, me.GetProperty("email").GetString());

        // 地址的主人仍能用它建号
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = "victim_owner",
            Email = victimEmail,
            Password,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
    }

    /// <summary>
    /// 以已验证邮箱新建的账号记为已确认：本人再用另一个外部账号（同一已验证邮箱）登录，关联回同一个用户，而不是被拒。
    /// </summary>
    [Fact]
    public async Task Account_created_from_a_verified_email_links_the_next_verified_sign_in()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        const string email = "owner@example.test";

        provider.User = External("gh-first") with { Email = email, EmailVerified = true };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        firstClient.DefaultRequestHeaders.Add("Cookie", CookieOf(first));
        var firstMe = await firstClient.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(email, firstMe.GetProperty("email").GetString());

        provider.User = External("gh-second") with { Email = email, EmailVerified = true };
        using var second = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        secondClient.DefaultRequestHeaders.Add("Cookie", CookieOf(second));
        var secondMe = await secondClient.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(firstMe.GetProperty("id").GetGuid(), secondMe.GetProperty("id").GetGuid());
    }

    /// <summary>没有公开句柄的提供商（Google 形态）：用户名由显示名派生，不含邮箱本地部。</summary>
    /// <remarks>
    /// 用户名是公开标识符，出现在每个展示位上；拿邮箱本地部当用户名等于把半个联系方式公开。
    /// 这条钉的是领域层的基底回落（句柄缺失时取显示名）：把回落改成取
    /// <c>ProviderAccountLabel</c>（Google 那边就是邮箱）会让它变红。
    /// 提供商自己不许从邮箱推断句柄，由 <c>OAuthProviderUserInfoMappingTests</c> 钉住。
    /// </remarks>
    [Fact]
    public async Task A_provider_without_a_handle_does_not_put_the_email_local_part_in_the_username()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        // 形如 Google：有邮箱与姓名，没有句柄
        provider.User = External("goog-1") with
        {
            SuggestedUsername = null,
            ProviderAccountLabel = "zhangsan@example.test",
            Email = "zhangsan@example.test",
            EmailVerified = true,
            DisplayName = "Zhang San"
        };

        using var response = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var username = await UsernameOfAsync(host, "zhangsan@example.test");
        Assert.DoesNotContain("zhangsan", username, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("zhang_san", username);
    }

    /// <summary>基底被占用时第二个人拿到带数字后缀的用户名，而不是插入失败。</summary>
    /// <remarks>
    /// <c>Username</c> 有唯一索引（按租户过滤）。直接用提供商给的值，两个不同域的同名用户
    /// （<c>alice@x.com</c> 与 <c>alice@y.com</c>）会让第二个人首次登录时插入失败。
    /// 去掉后缀、把裸基底发出去，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_colliding_username_base_gets_a_numeric_suffix()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        provider.User = External("gh-dup-1") with { SuggestedUsername = "alice", Email = "alice@x.test", EmailVerified = true };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 另一个人，同样的基底，不同邮箱
        provider.User = External("gh-dup-2") with { SuggestedUsername = "alice", Email = "alice@y.test", EmailVerified = true };
        using var second = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstName = await UsernameOfAsync(host, "alice@x.test");
        var secondName = await UsernameOfAsync(host, "alice@y.test");
        Assert.Equal("alice", firstName);
        Assert.Matches("^alice_[0-9]{6}$", secondName);
    }

    /// <summary>生成出来的用户名必须是用户自己在账号设置里也能填的形态。</summary>
    /// <remarks>
    /// 提供商给的值可能含 <c>.</c> <c>+</c> 这类字符（<c>alice.smith+tag</c>），
    /// 不符合 <see cref="UsernameRules"/>，会造出用户改不回去、API 也建不出来的账号。
    /// 去掉清洗，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_generated_username_satisfies_the_public_username_rules()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        provider.User = External("gh-odd") with
        {
            SuggestedUsername = "alice.smith+tag",
            Email = "odd@example.test",
            EmailVerified = true
        };

        using var response = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var username = await UsernameOfAsync(host, "odd@example.test");
        Assert.Matches(UsernameRules.Pattern, username);
        Assert.InRange(username.Length, UsernameRules.MinLength, UsernameRules.MaxLength);
    }

    /// <summary>显示名清洗后不可用（纯中文）时回落到 <c>user</c>，并且不发裸名。</summary>
    /// <remarks>
    /// 这是面向中日用户的默认路径，不是边角场景：这类用户全都归到同一个基底。
    /// 裸的 <c>user</c> 既没有识别价值，又会让后面每个人都撞在它上面。
    /// 让回落基底也走裸名分支，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_display_name_without_usable_characters_falls_back_to_a_suffixed_username()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        provider.User = External("goog-cjk") with
        {
            SuggestedUsername = null,
            ProviderAccountLabel = "zhangsan@example.test",
            Email = "zhangsan@example.test",
            EmailVerified = true,
            DisplayName = "张三"
        };

        using var response = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var username = await UsernameOfAsync(host, "zhangsan@example.test");
        Assert.Matches("^user_[0-9]{6}$", username);
    }

    /// <summary>被软删除的用户仍然占着用户名，生成器要避开它。</summary>
    /// <remarks>
    /// <c>Username</c> 的唯一索引没有排除 <c>IsDeleted</c>，软删除的行仍在表里占着名字，
    /// 而仓储默认把它们过滤掉。查重不关掉软删除过滤就会生成一个数据库拒绝的名字，
    /// 外部登录首次创建用户时直接失败。去掉生成器里的 <c>Disable&lt;ISoftDelete&gt;()</c>，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_username_held_by_a_soft_deleted_user_is_not_handed_out_again()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        provider.User = External("gh-del-1") with { SuggestedUsername = "alice", Email = "alice@x.test", EmailVerified = true };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("alice", await UsernameOfAsync(host, "alice@x.test"));

        await SoftDeleteUserAsync(host, "alice@x.test");

        provider.User = External("gh-del-2") with { SuggestedUsername = "alice", Email = "alice@y.test", EmailVerified = true };
        using var second = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Matches("^alice_[0-9]{6}$", await UsernameOfAsync(host, "alice@y.test"));
    }

    /// <summary>邮箱未验证时填的占位地址不随用户名走：用户改名后，下一个同基底的人不会撞邮箱唯一索引。</summary>
    /// <remarks>
    /// 用户名可以改，邮箱不跟着改。占位地址若由用户名派生，改名就把基底释放了出去，
    /// 下一个拿到同一个用户名的人会算出同一个占位地址，撞上 <c>Email</c> 的唯一索引。
    /// 把占位地址改回由用户名派生，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_placeholder_email_survives_a_username_change_by_the_previous_holder()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        // 甲：句柄 alice，邮箱未验证，于是邮箱是占位地址
        provider.User = External("gh-ph-1") with { SuggestedUsername = "alice", Email = "alice@x.test", EmailVerified = false };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 甲改名，把 alice 这个基底释放出去；占位邮箱留在甲身上
        var firstPlaceholder = await RenameUserAsync(host, "alice", "bob");

        // 乙：另一个外部账号，句柄同样是 alice，邮箱同样未验证
        provider.User = External("gh-ph-2") with { SuggestedUsername = "alice", Email = "alice@y.test", EmailVerified = false };
        using var second = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondPlaceholder = await PlaceholderEmailOfAsync(host, "alice");
        Assert.NotEqual(firstPlaceholder, secondPlaceholder);
    }

    /// <summary>已验证邮箱属于一个被删除的账号时，拒绝而不是带着同一个地址另建一个。</summary>
    /// <remarks>
    /// <c>Email</c> 的唯一索引没有排除 <c>IsDeleted</c>，被删用户仍然占着地址。
    /// 按邮箱关联已有用户时若只看未删除的行，这里会判定"没人用过"，随后建号落库撞唯一索引返回 500。
    /// 去掉这段查找上的 <c>Disable&lt;ISoftDelete&gt;()</c>，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_verified_email_owned_by_a_deleted_account_is_refused_instead_of_duplicated()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        var username = await CreateUserAsync(host, "deleted_owner");
        var email = $"{username}@example.test";
        await SoftDeleteUserAsync(host, email);

        provider.User = External("gh-deleted-owner") with
        {
            SuggestedUsername = "someone",
            Email = email,
            EmailVerified = true
        };

        using var response = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ExternalAuth:EmailOwnedByDeletedAccount", await ErrorCodeAsync(response));
        // 本地化形态下词条会整条替换抛出时的消息：两种形态都必须说清"账号已删除"，且不留未填充的占位符
        var detail = await DetailAsync(response);
        Assert.DoesNotContain("{", detail, StringComparison.Ordinal);
        Assert.Contains("deleted", detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>解绑之后用同一个外部账号重新登录，不能撞上残留用户行的占位邮箱。</summary>
    /// <remarks>
    /// 外部连接可以解绑（<c>UnlinkAsync</c> 只要求还剩一种登录方式），而用户行连同它的占位邮箱一直在。
    /// 占位地址若由外部账号的标识派生，重新登录时算出的就是同一个地址，撞上 <c>Email</c> 的唯一索引。
    /// 把占位地址改成由 provider + providerId 派生，这条会红（由用户名派生则是上一条用例变红）。
    /// </remarks>
    [Fact]
    public async Task Relinking_after_an_unlink_does_not_collide_with_the_abandoned_placeholder_email()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);

        provider.User = External("gh-relink") with { SuggestedUsername = "alice", Email = "alice@x.test", EmailVerified = false };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var abandoned = await PlaceholderEmailOfAsync(host, "alice");

        // 有了密码才允许解绑最后一个外部登录
        await SetPasswordAsync(host, "alice");
        using var session = ProjectWebApplicationFactory.CreateProjectClient(host);
        session.DefaultRequestHeaders.Add("Cookie", CookieOf(first));
        using var unlink = await session.DeleteAsync($"/api/v1/external-auth/links/{LinkIdOf(await ReadLinksAsync(session))}");
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);

        // 同一个外部账号重新登录：连接已不在，于是新建用户，占位邮箱必须与被遗弃的那个不同
        using var again = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var rebound = await PlaceholderEmailOfAsync(host, (await UsernamesOfPlaceholdersAsync(host)).Single(u => u != "alice"));
        Assert.NotEqual(abandoned, rebound);
    }

    private static async Task SetPasswordAsync(WebApplicationFactory<Program> host, string username)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var user = await repository.GetOneAsync(u => u.Username == username);
        scope.ServiceProvider.GetRequiredService<UserDomainService>().ResetPassword(user!, Password);
        await repository.UpdateAsync(user!);
        await unitOfWork.CompleteAsync();
    }

    private static async Task<List<string>> UsernamesOfPlaceholdersAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var users = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .GetListAsync(u => u.Email.EndsWith(".local"));
        await unitOfWork.CompleteAsync();
        return users.Select(u => u.Username).ToList();
    }

    /// <summary>把用户名换掉，邮箱保持不变；返回它的邮箱。</summary>
    private static async Task<string> RenameUserAsync(
        WebApplicationFactory<Program> host,
        string username,
        string newUsername)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var user = await repository.GetOneAsync(u => u.Username == username);
        user!.UpdateProfile(newUsername, user.Email, user.DisplayName, user.PhoneNumber);
        await repository.UpdateAsync(user);
        await unitOfWork.CompleteAsync();
        return user.Email;
    }

    private static async Task<string> PlaceholderEmailOfAsync(WebApplicationFactory<Program> host, string username)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var user = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .GetOneAsync(u => u.Username == username);
        await unitOfWork.CompleteAsync();
        return user!.Email;
    }

    private static async Task SoftDeleteUserAsync(WebApplicationFactory<Program> host, string email)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var user = await repository.GetOneAsync(u => u.Email == email);
        await repository.DeleteAsync(user!);
        await unitOfWork.CompleteAsync();
    }

    private static async Task<string> UsernameOfAsync(WebApplicationFactory<Program> host, string email)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var user = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
            .GetOneAsync(u => u.Email == email);
        await unitOfWork.CompleteAsync();
        return user!.Username;
    }

    private static async Task<HttpResponseMessage> SignInExternallyAsync(WebApplicationFactory<Program> host)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(client, "/api/v1/external-auth/github/login-url");
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return await client.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
    }

    private static async Task<string> ReadSecurityStampAsync(WebApplicationFactory<Program> host, string username)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var user = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().GetOneAsync(u => u.Username == username);
        return user!.SecurityStamp;
    }

    private static WebApplicationFactory<Program> CreateHost(ProjectWebApplicationFactory factory, SwitchableOAuthProvider provider) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalAuth:Github:ClientId"] = "integration-test-client",
                    ["ExternalAuth:Github:ClientSecret"] = "integration-test-secret",
                    ["ExternalAuth:Github:RedirectUri"] = "https://client.example.test/auth/external-callback"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOAuthProvider>();
                services.AddSingleton<IOAuthProvider>(provider);
            });
        });

    private static async Task<HttpResponseMessage> LinkAsync(WebApplicationFactory<Program> host, AuthenticatedSession session)
    {
        var (state, stateCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/link-url");
        return await PostWithCookiesAsync(host, $"{session.Cookie}; {stateCookie}",
            "/api/v1/external-auth/github/link", new { Code = "code", State = state });
    }

    private static async Task<(string State, string Cookie)> StartAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var state = QueryHelpers.ParseQuery(new Uri(body.RootElement.GetProperty("loginUrl").GetString()!).Query)["state"].ToString();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return (state, cookie);
    }

    private static async Task<HttpResponseMessage> PostWithCookiesAsync(
        WebApplicationFactory<Program> host, string cookies, string url, object body)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadLinksAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/external-auth/links"));
        return body.RootElement.Clone();
    }

    private static JsonElement GithubLink(JsonElement links) =>
        links.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "github")
            .TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.Object ? link : default;

    private static Guid LinkIdOf(JsonElement links) => GithubLink(links).GetProperty("id").GetGuid();

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "";
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string CookieOf(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Where(value => !value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

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

    private static ExternalUserInfo External(string id) => new()
    {
        ProviderId = id,
        ProviderAccountLabel = id,
        SuggestedUsername = id,
        Email = $"{id}@external.example.test"
    };

    /// <summary>测试替身：每次授权"回来"的外部身份由用例指定。</summary>
    private sealed class SwitchableOAuthProvider : IOAuthProvider
    {
        public ExternalUserInfo User { get; set; } = new() { ProviderId = "unset", ProviderAccountLabel = "unset" };

        public string Name => "github";

        public bool IsAvailable => true;

        public string GetAuthorizationUrl(string state) =>
            $"https://provider.example.test/authorize?state={Uri.EscapeDataString(state)}";

        public Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
            string code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuthTokenInfo { AccessToken = "integration-test-token" });

        public Task<ExternalUserInfo> GetUserInfoAsync(
            string accessToken,
            CancellationToken cancellationToken = default) => Task.FromResult(User);
    }
}
#endif
