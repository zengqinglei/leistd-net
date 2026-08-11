using System.Security.Claims;
using Leistd.ServiceClient.Constants;
using Leistd.ServiceClient.Handlers;
using Leistd.ServiceClient.Options;
using Leistd.TestBase;
using Xunit;

namespace Leistd.ServiceClient.Tests;

public class UserContextForwardingTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<HttpRequestMessage> SendAsync(
        FakeCurrentUser user, UserContextForwardingOptions options, HttpRequestMessage? request = null)
    {
        var capture = new CaptureHandler();
        var handler = new UserContextDelegatingHandler(user, options) { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(request ?? new HttpRequestMessage(HttpMethod.Get, "http://demo/api"), CancellationToken.None);
        return capture.Requests.Single();
    }

    [Fact]
    public async Task 认证用户_注入用户Id与URL编码的用户名()
    {
        var user = new FakeCurrentUser(id: UserId, username: "张三");

        var sent = await SendAsync(user, new UserContextForwardingOptions());

        Assert.Equal(UserId.ToString(), sent.Headers.GetValues(ServiceClientHeaders.UserId).Single());
        var encodedName = sent.Headers.GetValues(ServiceClientHeaders.UserName).Single();
        Assert.Equal("张三", Uri.UnescapeDataString(encodedName));
        Assert.DoesNotContain('张', encodedName); // 头值必须是 ASCII 安全的
    }

    [Fact]
    public async Task 请求已有同名头_不覆盖()
    {
        var user = new FakeCurrentUser(id: UserId, username: "someone");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://demo/api");
        request.Headers.Add(ServiceClientHeaders.UserId, "preset");

        var sent = await SendAsync(user, new UserContextForwardingOptions(), request);

        Assert.Equal("preset", sent.Headers.GetValues(ServiceClientHeaders.UserId).Single());
    }

    [Fact]
    public async Task 未认证用户_不注入任何头()
    {
        var sent = await SendAsync(new FakeCurrentUser(), new UserContextForwardingOptions());

        Assert.False(sent.Headers.Contains(ServiceClientHeaders.UserId));
        Assert.False(sent.Headers.Contains(ServiceClientHeaders.UserName));
    }

    [Fact]
    public async Task 关闭转发_不注入任何头()
    {
        var user = new FakeCurrentUser(id: UserId, username: "someone");

        var sent = await SendAsync(user, new UserContextForwardingOptions { Enable = false });

        Assert.False(sent.Headers.Contains(ServiceClientHeaders.UserId));
    }

    [Fact]
    public async Task 关闭用户名转发_只注入用户Id()
    {
        var user = new FakeCurrentUser(id: UserId, username: "someone");

        var sent = await SendAsync(user, new UserContextForwardingOptions { ForwardUserName = false });

        Assert.True(sent.Headers.Contains(ServiceClientHeaders.UserId));
        Assert.False(sent.Headers.Contains(ServiceClientHeaders.UserName));
    }

    [Fact]
    public async Task 自定义Claim映射_按配置注入并编码()
    {
        var user = new FakeCurrentUser(
            id: UserId,
            claims: [new Claim("tenant", "租户A")]);
        var options = new UserContextForwardingOptions
        {
            ClaimHeaderMap = { ["tenant"] = "X-Tenant" },
        };

        var sent = await SendAsync(user, options);

        Assert.Equal("租户A", Uri.UnescapeDataString(sent.Headers.GetValues("X-Tenant").Single()));
    }
}
