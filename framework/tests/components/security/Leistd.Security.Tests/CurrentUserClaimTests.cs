using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.Security.Clients;
using Leistd.Security.Users;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Security.Tests;

/// <summary>
/// 主体到强类型身份的解析：缺失、格式错误、多值与优先级。
/// </summary>
/// <remarks>
/// 这些分支不是理论风险——多租户家族专门有 <c>InvalidTenantClaimException</c>，
/// 说明"同一个 claim type 出现多个值"在生产里确实发生过。
/// </remarks>
public class CurrentUserClaimTests
{
    private sealed class FixedPrincipalAccessor(ClaimsPrincipal? principal) : ICurrentPrincipalAccessor
    {
        public ClaimsPrincipal? Principal { get; } = principal;
        public IDisposable Change(ClaimsPrincipal principal) => throw new NotSupportedException();
    }

    private static readonly IOptions<ClaimTypeOptions> DefaultClaimTypes = Options.Create(new ClaimTypeOptions());

    private static ICurrentUser User(params Claim[] claims) =>
        new CurrentUser(new FixedPrincipalAccessor(
            new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"))), DefaultClaimTypes);

    private static ICurrentUser Anonymous() => new CurrentUser(new FixedPrincipalAccessor(null), DefaultClaimTypes);

    [Fact]
    public void No_principal_yields_an_empty_identity_rather_than_throwing()
    {
        var user = Anonymous();

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.Id);
        Assert.Null(user.TenantId);
        Assert.Null(user.Username);
        Assert.Null(user.Name);
        Assert.Null(user.Email);
        Assert.Null(user.FindClaim("sub"));
    }

    // 未认证的主体（无 authenticationType）不得报告已认证——授权判定直接依赖这个属性。
    [Fact]
    public void Unauthenticated_identity_is_reported_as_such()
    {
        var user = new CurrentUser(new FixedPrincipalAccessor(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]))), DefaultClaimTypes);

        Assert.False(user.IsAuthenticated);
        Assert.NotNull(user.Id);   // 身份仍可读，只是不算已认证
    }

    // sub 不是 GUID 时必须返回 null，不能抛也不能给个零 Guid——
    // 零 Guid 会被下游当成一个真实用户，静默把数据归给"用户 00000000-...".
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("client:worker-1")]
    public void Non_guid_subject_yields_no_user_id(string subject)
    {
        Assert.Null(User(new Claim("sub", subject)).Id);
    }

    // sub 优先于 NameIdentifier：OIDC 主体两者都有时必须取 sub。
    [Fact]
    public void Subject_wins_over_name_identifier()
    {
        var sub = Guid.CreateVersion7();

        var user = User(
            new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
            new Claim("sub", sub.ToString()));

        Assert.Equal(sub, user.Id);
    }

    [Fact]
    public void Name_identifier_is_the_fallback_when_subject_is_absent()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(id, User(new Claim(ClaimTypes.NameIdentifier, id.ToString())).Id);
    }

    // 同一 claim type 多值时取第一个，且不得抛——签发端配置错误不应让整个请求 500。
    [Fact]
    public void Duplicate_claims_resolve_to_the_first_value()
    {
        var first = Guid.CreateVersion7();

        var user = User(
            new Claim("sub", first.ToString()),
            new Claim("sub", Guid.CreateVersion7().ToString()));

        Assert.Equal(first, user.Id);
    }

    [Theory]
    [InlineData("preferred_username", "pu")]
    [InlineData("name", "n")]
    public void Username_prefers_preferred_username_then_name(string claimType, string expected)
    {
        Assert.Equal(expected, User(new Claim(claimType, expected)).Username);
    }

    [Fact]
    public void Username_prefers_preferred_username_over_name_when_both_exist()
    {
        var user = User(new Claim("name", "display"), new Claim("preferred_username", "login"));

        Assert.Equal("login", user.Username);
        Assert.Equal("display", user.Name);
    }

    // 空白值必须被跳过继续找下一个候选，否则一个空的 preferred_username 会把 name 遮住。
    [Fact]
    public void Blank_values_fall_through_to_the_next_candidate()
    {
        var user = User(new Claim("preferred_username", "   "), new Claim("name", "display"));

        Assert.Equal("display", user.Username);
    }

    [Fact]
    public void Tenant_id_reads_a_single_guid_claim_and_none_means_host()
    {
        Assert.Null(User(new Claim("sub", Guid.NewGuid().ToString())).TenantId);

        var tenant = Guid.CreateVersion7();
        Assert.Equal(tenant, User(new Claim(CustomClaimTypes.TenantId, tenant.ToString())).TenantId);
    }

    // 非法租户 claim 失败关闭：当成宿主等于让租户主体看到宿主数据
    [Fact]
    public void An_invalid_tenant_claim_is_rejected_rather_than_read_as_host()
    {
        Assert.Throws<InvalidOperationException>(() => User(new Claim(CustomClaimTypes.TenantId, "host")).TenantId);
        Assert.Throws<InvalidOperationException>(() => User(
            new Claim(CustomClaimTypes.TenantId, Guid.CreateVersion7().ToString()),
            new Claim(CustomClaimTypes.TenantId, Guid.CreateVersion7().ToString())).TenantId);
    }

    [Fact]
    public void Email_reads_its_own_claim_type()
    {
        Assert.Equal("a@example.test", User(new Claim("email", "a@example.test")).Email);
    }

    // 机器主体的 sub 带前缀，解析成用户 Id 必须失败——否则可自定义的 client_id
    // 会被当成用户身份，这是一条越权路径。
    [Fact]
    public void Machine_subject_is_never_parsed_as_a_user()
    {
        var accessor = new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", ClientSubject.Format("reporting-worker")),
                new Claim(CustomClaimTypes.ClientId, "reporting-worker"),
            ],
            authenticationType: "Test")));

        Assert.Null(new CurrentUser(accessor, DefaultClaimTypes).Id);

        var client = new CurrentClient(accessor);
        Assert.True(client.IsAuthenticated);
        Assert.Equal("reporting-worker", client.ClientId);
    }

    [Fact]
    public void Client_without_a_client_id_claim_is_not_authenticated()
    {
        var client = new CurrentClient(new FixedPrincipalAccessor(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x")], "Test"))));

        Assert.False(client.IsAuthenticated);
        Assert.Null(client.ClientId);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("worker-1", false)]
    [InlineData("client:worker-1", true)]
    public void Machine_subject_detection_only_looks_at_the_prefix(string? subject, bool expected)
    {
        Assert.Equal(expected, ClientSubject.IsMachine(subject));
    }

    [Theory]
    [InlineData("client:worker-1", "worker-1", true)]
    [InlineData("client:worker-1", "worker-2", false)]
    [InlineData("worker-1", "worker-1", false)]
    [InlineData(null, "worker-1", false)]
    [InlineData("client:worker-1", null, false)]
    [InlineData("client:worker-1", "", false)]
    public void Machine_subject_match_requires_both_sides(string? subject, string? clientId, bool expected)
    {
        Assert.Equal(expected, ClientSubject.Matches(subject, clientId));
    }
}
