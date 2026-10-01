#if (ExternalLogin)
using System.Net;
using System.Text;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Infrastructure;

/// <summary>
/// 提供商响应到 <see cref="ExternalUserInfo"/> 的映射：取值要按各自 API 的字段，
/// 并且只有真正的公开句柄才能进 <see cref="ExternalUserInfo.SuggestedUsername"/>。
/// </summary>
public sealed class OAuthProviderUserInfoMappingTests
{
    private const string GitHubUser = """{"id":42,"login":"octo","email":"public@example.test"}""";

    [Fact]
    public async Task GitHub_reports_the_primary_email_and_its_verification()
    {
        var provider = GitHub(request => request.RequestUri!.AbsolutePath == "/user/emails"
            ? Json("""[{"email":"other@example.test","primary":false,"verified":true},{"email":"primary@example.test","primary":true,"verified":true}]""")
            : Json(GitHubUser));

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("primary@example.test", user.Email);
        Assert.True(user.EmailVerified);
    }

    [Fact]
    public async Task GitHub_unverified_primary_email_is_reported_as_unverified()
    {
        var provider = GitHub(request => request.RequestUri!.AbsolutePath == "/user/emails"
            ? Json("""[{"email":"primary@example.test","primary":true,"verified":false}]""")
            : Json(GitHubUser));

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("primary@example.test", user.Email);
        Assert.False(user.EmailVerified);
    }

    /// <summary>邮箱接口失败不中断登录：回落到 /user 的公开邮箱，按未验证处理。</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task GitHub_email_lookup_failure_falls_back_to_the_unverified_public_email(string failure)
    {
        var provider = GitHub(request => request.RequestUri!.AbsolutePath != "/user/emails"
            ? Json(GitHubUser)
            : failure switch
            {
                "status" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                "network" => throw new HttpRequestException("connection reset"),
                "timeout" => throw new TaskCanceledException("timed out"),
                _ => Json("<html>not json</html>")
            });

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("public@example.test", user.Email);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task GitHub_email_lookup_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = GitHub(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/user/emails")
                return Json(GitHubUser);

            cancellation.Cancel();
            throw new TaskCanceledException("canceled", null, cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetUserInfoAsync("token", cancellation.Token));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", false)]
    public async Task Google_reads_verified_email_as_a_json_boolean(string verifiedEmail, bool expected)
    {
        var provider = Google($$"""{"id":"g-1","email":"user@example.test","verified_email":{{verifiedEmail}}}""");

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("user@example.test", user.Email);
        Assert.Equal(expected, user.EmailVerified);
    }

    /// <summary>GitHub 的 login 是设计上的公开句柄，可以直接当本地用户名的基底。</summary>
    [Fact]
    public async Task GitHub_offers_its_public_handle_as_a_username_base()
    {
        var provider = GitHub(_ => Json(GitHubUser));

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("octo", user.ProviderAccountLabel);
        Assert.Equal("octo", user.SuggestedUsername);
    }

    /// <summary>
    /// Google 没有句柄，只有邮箱和姓名。邮箱只能当展示标签，不能顺手截本地部当用户名基底——
    /// 那会把半个邮箱变成公开标识符，而且不同域的同名用户会撞上 Username 的唯一索引。
    /// </summary>
    [Fact]
    public async Task Google_offers_no_username_base_and_never_derives_one_from_the_email()
    {
        var provider = Google("""{"id":"g-1","email":"zhangsan@example.test","verified_email":true,"name":"Zhang San"}""");

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("zhangsan@example.test", user.ProviderAccountLabel);
        Assert.Null(user.SuggestedUsername);
    }

    /// <summary>标签要始终有值（界面上要显示"绑定了哪个账号"），没有邮箱时回落到提供商 ID。</summary>
    [Fact]
    public async Task Google_without_an_email_labels_the_account_with_the_provider_id()
    {
        var provider = Google("""{"id":"g-1","name":"Zhang San"}""");

        var user = await provider.GetUserInfoAsync("token");

        Assert.Equal("g-1", user.ProviderAccountLabel);
        Assert.Null(user.SuggestedUsername);
    }

    private static GoogleOAuthProvider Google(string body) =>
        new(new StubHttpClientFactory(_ => Json(body)),
            Options.Create(new ExternalAuthOptions()),
            NullLogger<GoogleOAuthProvider>.Instance);

    private static GitHubOAuthProvider GitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHttpClientFactory(respond), Options.Create(new ExternalAuthOptions()), NullLogger<GitHubOAuthProvider>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
#endif
