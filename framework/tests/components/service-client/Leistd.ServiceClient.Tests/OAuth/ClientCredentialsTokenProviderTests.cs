using System.Net;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.OAuth.Options;
using Leistd.ServiceClient.OAuth.Services;
using Leistd.ServiceClient.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.ServiceClient.OAuth.Abstractions;
using Leistd.TestBase.Doubles;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.Tests.OAuth;

public class ClientCredentialsTokenProviderTests
{
    private const string ClientName = "DemoService";

    private static (IServiceTokenProvider Provider, CapturingHttpMessageHandler TokenEndpoint) Create(
        Action<ClientCredentialsOptions>? configure = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null,
        TimeProvider? timeProvider = null)
    {
        var tokenEndpoint = new CapturingHttpMessageHandler();
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
        if (timeProvider is null)
        {
            services.AddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        }
        else
        {
            services.AddSingleton<IServiceTokenProvider>(sp => new ClientCredentialsTokenProvider(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IOptionsMonitor<ClientCredentialsOptions>>(),
                sp.GetRequiredService<ILogger<ClientCredentialsTokenProvider>>(),
                timeProvider));
        }

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceTokenProvider>(), tokenEndpoint);
    }

    // 缓存过期判定与"提前 ExpirationBuffer 刷新"都走注入的时间源。推进到缓冲区内必须换新令牌
    // ——否则这条逻辑只能靠真的等到过期才能验证，本轮加的时间接缝也就只是摆设。
    [Fact]
    public async Task Token_is_refetched_within_the_expiry_buffer()
    {
        var time = new FakeTimeProvider();
        var (provider, endpoint) = Create(
            options =>
            {
                options.Authority = "http://identity";
                options.ClientId = "svc-a";
                options.ClientSecret = "secret";
                options.ExpirationBuffer = TimeSpan.FromSeconds(60);
            },
            timeProvider: time);

        var first = await provider.GetAccessTokenAsync(ClientName);
        var cached = await provider.GetAccessTokenAsync(ClientName);
        Assert.Equal(first, cached);
        Assert.Single(endpoint.Requests);

        // expires_in 是 3600 秒，缓冲 60 秒：推进到 3550 秒时已进入缓冲区
        time.Advance(TimeSpan.FromSeconds(3550));
        var refreshed = await provider.GetAccessTokenAsync(ClientName);

        Assert.NotEqual(first, refreshed);
        Assert.Equal(2, endpoint.Requests.Count);
    }

    [Fact]
    public async Task Token_request_uses_the_standard_client_credentials_form()
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
    public async Task Cached_token_is_reused_while_valid()
    {
        var (provider, endpoint) = Create();

        var first = await provider.GetAccessTokenAsync(ClientName);
        var second = await provider.GetAccessTokenAsync(ClientName);

        Assert.Equal(first, second);
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task Token_below_the_remaining_lifetime_buffer_is_refetched()
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
    public async Task Concurrent_requests_hit_the_endpoint_once()
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
    public async Task Invalidate_forces_a_new_token()
    {
        var (provider, endpoint) = Create();

        var first = await provider.GetAccessTokenAsync(ClientName);
        provider.Invalidate(ClientName);
        var second = await provider.GetAccessTokenAsync(ClientName);

        Assert.NotEqual(first, second);
        Assert.Equal(2, endpoint.Requests.Count);
    }

    [Fact]
    public async Task Endpoint_error_preserves_remote_status_without_classifying_local_failure()
    {
        var (provider, _) = Create(responder: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_client"}"""),
        });

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Contains("invalid_client", exception.Message);
        Assert.Equal(400, exception.RemoteStatusCode);
        Assert.Equal(ServiceClientFailureKind.RemoteFailure, exception.FailureKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Token_endpoint_status_does_not_become_a_local_transport_failure(HttpStatusCode remoteStatus)
    {
        var (provider, _) = Create(responder: _ => new HttpResponseMessage(remoteStatus)
        {
            Content = new StringContent("remote diagnostic"),
        });

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Equal((int)remoteStatus, exception.RemoteStatusCode);
        Assert.Equal(ServiceClientFailureKind.RemoteFailure, exception.FailureKind);
    }

    [Fact]
    public async Task Token_request_timeout_without_caller_cancellation_is_classified_as_timeout()
    {
        var (provider, _) = Create(responder: _ => throw new OperationCanceledException("token endpoint timed out"));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Equal(ServiceClientFailureKind.Timeout, exception.FailureKind);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ResponseEnded)]
    public async Task Malformed_or_incomplete_token_response_is_classified_as_invalid_response(HttpRequestError error)
    {
        var (provider, _) = Create(responder: _ => throw new HttpIOException(error, "token response ended"));

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Equal(ServiceClientFailureKind.InvalidResponse, exception.FailureKind);
        Assert.IsType<HttpIOException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ResponseEnded)]
    public async Task Token_response_body_failure_is_classified_while_send_async_buffers_the_response(HttpRequestError error)
    {
        var (provider, _) = Create(responder: _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingHttpContent(new HttpIOException(error, "token response body ended")),
        });

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Equal(ServiceClientFailureKind.InvalidResponse, exception.FailureKind);
        var transportException = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.IsType<HttpIOException>(transportException.InnerException);
    }

    [Fact]
    public async Task Missing_endpoint_throws_ServiceClientException()
    {
        var (provider, _) = Create(options => options.ClientId = "svc-a");

        var exception = await Assert.ThrowsAsync<ServiceClientException>(() =>
            provider.GetAccessTokenAsync(ClientName));

        Assert.Contains("Authority or TokenEndpoint", exception.Message);
    }
}
