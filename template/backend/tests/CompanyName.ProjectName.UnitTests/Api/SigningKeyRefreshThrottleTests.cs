#if (RemoteTokenAuth)
using CompanyName.ProjectName.Api.Auth.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>请求刷新在最短间隔内只转交一次：签发方不可用时，伪造 kid 的请求不能让每个请求都去抓一次 JWKS。</summary>
public sealed class SigningKeyRefreshThrottleTests
{
    [Fact]
    public void Refresh_requests_are_forwarded_at_most_once_per_interval()
    {
        var inner = new CountingManager();
        var time = new FakeTimeProvider();
        var throttle = new SigningKeyRefreshThrottle(inner, time, NullLogger<SigningKeyRefreshThrottle>.Instance);

        for (var call = 0; call < 50; call++) throttle.RequestRefresh();
        Assert.Equal(1, inner.Refreshes);
        time.Advance(SigningKeyRefreshThrottle.MinimumInterval - TimeSpan.FromSeconds(1));
        throttle.RequestRefresh();
        Assert.Equal(1, inner.Refreshes);
        time.Advance(TimeSpan.FromSeconds(1));
        throttle.RequestRefresh();
        Assert.Equal(2, inner.Refreshes);
    }

    private sealed class CountingManager : IConfigurationManager<OpenIddictConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIddictConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(new OpenIddictConfiguration());

        public void RequestRefresh() => Refreshes++;
    }
}
#endif
