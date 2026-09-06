#if (RemoteTokenAuth)
using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Api.HostedServices.Initializer;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 资源服务启动门禁的语义：确认前拒绝流量，确认后锁存
/// </summary>
/// <remarks>
/// 集成测试宿主会把门禁预置为已开（那里没有真实 Identity，探针永远探不通），
/// 所以门禁本身必须单独钉——否则"预置已开"会把这条能力整个遮掉，
/// 上线后没人知道它到底拦不拦。
/// </remarks>
public class ResourceReadinessGateTests
{
    private const string Issuer = "https://identity.example.com/";
    private static readonly Uri MetadataUrl = new("https://identity.example.com/.well-known/openid-configuration");

    /// <summary>
    /// 只返回 2xx 不算确认
    /// </summary>
    /// <remarks>
    /// 反向代理的错误页、登录跳转页、返回 200 的占位服务都能通过
    /// <c>EnsureSuccessStatusCode()</c>。门禁一旦被它们骗开，Pod 就进流量，
    /// 而第一个已认证请求必然失败——问题只是从启动期推迟到了线上。
    /// </remarks>
    [Fact]
    public async Task A_two_hundred_that_is_not_a_discovery_document_is_rejected()
    {
        using var client = ClientReturning(("/.well-known/openid-configuration", "<html>login</html>"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None));
    }

    /// <summary>issuer 对不上必须拒绝：令牌校验一定会失败</summary>
    [Fact]
    public async Task A_discovery_document_declaring_a_different_issuer_is_rejected()
    {
        using var client = ClientReturning(
            ("/.well-known/openid-configuration",
             """{"issuer":"https://someone-else.example.com/","jwks_uri":"https://identity.example.com/jwks"}"""),
            ("/jwks", """{"keys":[{"kty":"RSA","kid":"k1"}]}"""));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None));
        Assert.Contains("someone-else", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_discovery_document_without_a_jwks_uri_is_rejected()
    {
        using var client = ClientReturning(
            ("/.well-known/openid-configuration", """{"issuer":"https://identity.example.com/"}"""));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None));
        Assert.Contains("jwks_uri", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>JWKS 里没有密钥时，令牌校验不可能成功</summary>
    [Fact]
    public async Task An_empty_key_set_is_rejected()
    {
        using var client = ClientReturning(
            ("/.well-known/openid-configuration",
             """{"issuer":"https://identity.example.com/","jwks_uri":"https://identity.example.com/jwks"}"""),
            ("/jwks", """{"keys":[]}"""));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None));
        Assert.Contains("no signing keys", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>三项都成立才算确认通过</summary>
    [Fact]
    public async Task A_valid_discovery_document_and_key_set_confirms()
    {
        using var client = ClientReturning(
            ("/.well-known/openid-configuration",
             """{"issuer":"https://identity.example.com/","jwks_uri":"https://identity.example.com/jwks"}"""),
            ("/jwks", """{"keys":[{"kty":"RSA","kid":"k1"}]}"""));

        await RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None);
    }

    /// <summary>
    /// issuer 只在大小写或尾斜杠上不同，也必须拒绝
    /// </summary>
    /// <remarks>
    /// 令牌校验器按配置的规范值原样比对。门禁比它宽松只会放行一个随后必然拒绝令牌的部署，
    /// 把失败从启动期推迟到线上——恰好抵消门禁存在的理由。
    /// 归一化属于配置侧（只保留一种规范写法），不属于校验侧。
    /// </remarks>
    [Theory]
    [InlineData("https://identity.example.com")]
    [InlineData("https://Identity.Example.com/")]
    [InlineData("https://identity.example.com//")]
    public async Task An_issuer_differing_only_in_case_or_trailing_slash_is_rejected(string declared)
    {
        using var client = ClientReturning(
            ("/.well-known/openid-configuration",
             $$"""{"issuer":"{{declared}}","jwks_uri":"https://identity.example.com/jwks"}"""),
            ("/jwks", """{"keys":[{"kty":"RSA","kid":"k1"}]}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteIdentityReadinessInitializer.ConfirmAsync(client, Issuer, MetadataUrl, CancellationToken.None));
    }

    private static HttpClient ClientReturning(params (string PathSuffix, string Body)[] responses) =>
        new(new StubHandler(responses));

    private sealed class StubHandler(IReadOnlyList<(string PathSuffix, string Body)> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var match = responses.FirstOrDefault(r => path.EndsWith(r.PathSuffix, StringComparison.Ordinal));

            return Task.FromResult(match.Body is null
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(match.Body)
                });
        }
    }

    [Fact]
    public async Task Readiness_is_unhealthy_until_remote_identity_is_confirmed()
    {
        var gate = new RemoteIdentityReadinessGate();
        var check = new RemoteIdentityReadinessCheck(gate);

        var before = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, before.Status);

        gate.MarkReady();

        var after = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }

    /// <summary>
    /// 确认之后锁存：不因签发方后续抖动回落
    /// </summary>
    /// <remarks>
    /// 这是刻意的取舍。持续同步探测签发方会让它一次短暂故障把已经在正常服务的实例
    /// 一起摘掉——那些实例的签名密钥与租户路由都还在缓存里，本来完全可以继续服务。
    /// 稳态 readiness 只回答"本进程已成功完成启动依赖确认"。
    /// </remarks>
    [Fact]
    public void Readiness_never_falls_back_once_confirmed()
    {
        var gate = new RemoteIdentityReadinessGate();
        gate.MarkReady();

        Assert.True(gate.IsReady);

        // 门禁没有、也不应该有"关回去"的入口
        Assert.Null(typeof(RemoteIdentityReadinessGate).GetMethod("MarkNotReady"));
        Assert.DoesNotContain(
            typeof(RemoteIdentityReadinessGate).GetProperties(),
            property => property.Name == nameof(RemoteIdentityReadinessGate.IsReady) && property.CanWrite);
    }
}
#endif
