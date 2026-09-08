using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Leistd.ServiceClient.OAuth.Handlers;
using Leistd.ServiceClient.OAuth.Services;
using Xunit;
using Leistd.ServiceClient.OAuth.Abstractions;
using Leistd.TestBase.Doubles;

namespace Leistd.ServiceClient.Tests.OAuth;

public class ClientCredentialsHandlerTests
{
    private sealed class FakeTokenProvider : IServiceTokenProvider
    {
        private int _generation = 1;
        public int Requests { get; private set; }
        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(string clientName, CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult($"token-gen{_generation}");
        }

        public void Invalidate(string clientName)
        {
            Invalidations++;
            _generation++;
        }
    }

    private static (HttpMessageInvoker Invoker, CapturingHttpMessageHandler Server, FakeTokenProvider Tokens) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var server = new CapturingHttpMessageHandler();
        if (responder is not null)
        {
            server.Responder = responder;
        }

        var tokens = new FakeTokenProvider();
        var handler = new ClientCredentialsDelegatingHandler("DemoService", tokens) { InnerHandler = server };
        return (new HttpMessageInvoker(handler), server, tokens);
    }

    [Fact]
    public async Task 出站请求_附加Bearer令牌()
    {
        var (invoker, server, _) = Create();

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        Assert.Equal("Bearer token-gen1", server.Requests.Single().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task 收到401_强刷令牌重试一次且请求体完整()
    {
        var (invoker, server, tokens) = Create(request =>
            request.Headers.Authorization!.Parameter == "token-gen1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Post, "http://demo/api")
        {
            Content = new StringContent("""{"amount":42}""", Encoding.UTF8, "application/json"),
        };
        var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal("Bearer token-gen2", server.Requests[1].Headers.Authorization!.ToString());
        Assert.Equal("""{"amount":42}""", server.RequestBodies[1]); // 重试请求体与原请求一致
        Assert.Equal("application/json", server.Requests[1].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task 重试后仍401_原样返回不再重试()
    {
        var (invoker, server, tokens) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact]
    public async Task 请求自带Authorization_不介入也不重试()
    {
        var (invoker, server, tokens) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://demo/api");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, tokens.Requests);
        Assert.Single(server.Requests);
    }
}
