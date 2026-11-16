#if (RemoteTokenAuth)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Infrastructure.Systems;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompanyName.ProjectName.UnitTests.Infrastructure;

public sealed class IdentitySystemDirectoryTests
{
    [Fact]
    public async Task Repeated_reads_share_the_short_lived_directory_cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var api = new FixedApi();
        var directory = new IdentitySystemDirectory(api, provider.GetRequiredService<HybridCache>(), NullLogger<IdentitySystemDirectory>.Instance);
        Assert.Single(await directory.GetListAsync());
        Assert.Single(await directory.GetListAsync());
        Assert.Equal(1, api.Calls);
    }

    private sealed class FixedApi : IIdentitySystemsApi
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage> GetListAsync(string culture, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new { key = "identity", displayName = "Identity", homeUrl = "https://identity.test/", isCurrent = true } })
            });
        }
    }
}
#endif
