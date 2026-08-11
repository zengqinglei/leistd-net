using System.Net;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.OAuth.Options;
using Leistd.ServiceClient.OAuth.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.ServiceClient.Tests;

public class ClientCredentialsTokenProviderTests
{
    private const string ClientName = "DemoService";

    private static (IServiceTokenProvider Provider, CaptureHandler TokenEndpoint) Create(
        Action<ClientCredentialsOptions>? configure = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var tokenEndpoint = new CaptureHandler();
        var issued = 0;
        tokenEndpoint.Responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"access_token":"token-{{Interlocked.Increment(ref issued)}}","token_type":"Bearer","expires_in":3600}"""),
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(ClientCredentialsTokenProvider.TokenHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint);
        services.Configure<ClientCredentialsOptions>(ClientName, configure ?? (options =>
        {
            options.Authority = "http://identity";
            options.ClientId = "svc-a";
            options.ClientSecret = "secret";
            options.Scope = "demo-api";
        }));
        services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceTokenProvider>(), tokenEndpoint);
    }

    [Fact]
    public async Task 获取令牌_按标准client_credentials形态请求默认端点()
    {
        var (provider, endpoint) = Create();

        var token = await provider.GetAccessTokenAsync(ClientName);

        Assert.Equal("token-1", token);
        var request = Assert.Single(endpoint.Requests);
        Assert.Equal("http://identity/connect/token", request.RequestUri!.ToString());
        var body = endpoint.RequestBodies.Single()!;
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("client_id=svc-a", body);
        Assert.Contains("client_secret=secret", body);
        Assert.Contains("scope=demo-api", body);
    }

    [Fact]
    public async Task 缓存有效期内_重复获取不再请求端点()
    {
        var (provider, endpoint) = Create();

        var first = await provider.GetAccessTokenAsync(ClientName);
        var second = await provider.GetAccessTokenAsync(ClientName);

        Assert.Equal(first, second);
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task 剩余有效期低于缓冲_视为过期并重新获取()
    {
        var (provider, endpoint) = Create(options =>
        {
            options.Authority = "http://identity";
            options.ClientId = "svc-a";
            options.ClientSecret = "secret";
            options.ExpirationBuffer = TimeSpan.FromSeconds(60);
        }, request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // expires_in 小于缓冲：每次获取都视为已过期
            Content = new StringContent("""{"access_token":"short-lived","expires_in":30}"""),
        });

        await provider.GetAccessTokenAsync(ClientName);
        await provider.GetAccessTokenAsync(ClientName);

        Assert.Equal(2, endpoint.Requests.Count);
    }

    [Fact]
    public async Task 并发获取_单飞只请求一次端点()
    {
        var gate = new TaskCompletionSource();
        var (provider, endpoint) = Create(responder: _ =>
        {
            gate.Task.Wait(TimeSpan.FromSeconds(5));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"concurrent","expires_in":3600}"""),
            };
        });

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => provider.GetAccessTokenAsync(ClientName)))
            .ToArray();
        gate.SetResult();
        var tokens = await Task.WhenAll(tasks);

        Assert.All(tokens, token => Assert.Equal("concurrent", token));
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task Invalidate后_重新获取新令牌()
    {
        var (provider, endpoint) = Create();

        var first = await provider.GetAccessTokenAsync(ClientName);
        provider.Invalidate(ClientName);
        var second = await provider.GetAccessTokenAsync(ClientName);

        Assert.NotEqual(first, second);
        Assert.Equal(2, endpoint.Requests.Count);
    }

    [Fact]
    public async Task 端点返回错误_抛ServiceClientException()
    {
        var (provider, _) = Create(responder: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_client"}"""),
        });

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Contains("invalid_client", exception.Message);
    }

    [Fact]
    public async Task 未配置ClientId_抛ServiceClientException()
    {
        var (provider, _) = Create(options => options.Authority = "http://identity");

        await Assert.ThrowsAsync<ServiceClientException>(() => provider.GetAccessTokenAsync(ClientName));
    }

    [Fact]
    public async Task 未配置端点_抛ServiceClientException()
    {
        var (provider, _) = Create(options => options.ClientId = "svc-a");

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Contains("Authority 或 TokenEndpoint", exception.Message);
    }
}
