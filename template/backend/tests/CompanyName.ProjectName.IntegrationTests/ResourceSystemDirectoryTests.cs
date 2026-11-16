#if (RemoteTokenAuth)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Systems.Dtos;
using CompanyName.ProjectName.Infrastructure.Systems;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ResourceSystemDirectoryTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task The_endpoint_caches_Identitys_directory_or_returns_empty_and_logs_a_missing_grant(HttpStatusCode status)
    {
        var api = new FixedApi(status);
        await using var host = factory.WithWebHostBuilder(builder =>
        {
#if (ResourceBrowserSession)
            builder.UseSetting("Authentication:ClientId", "orders-browser");
#endif
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentitySystemsApi>();
                services.AddSingleton<IIdentitySystemsApi>(api);
            });
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, Guid.CreateVersion7(), ProjectWebApplicationFactory.NewTenantId());
        var systems = (await session.Client.GetFromJsonAsync<List<SystemOutputDto>>("/api/v1/systems"))!;
        if (status == HttpStatusCode.OK)
        {
            Assert.Equal("identity", systems[0].Key);
#if (ResourceBrowserSession)
            Assert.True(systems.Single(system => system.Key == "orders-browser").IsCurrent);
            Assert.False(systems[0].IsCurrent);
#endif
            Assert.Equal(2, (await session.Client.GetFromJsonAsync<List<SystemOutputDto>>("/api/v1/systems"))!.Count);
            Assert.Equal(1, api.Calls);
        }
        else
        {
            Assert.Empty(systems);
            Assert.Contains(host.Services.GetFakeLogCollector().GetSnapshot(), log =>
                log.Level == LogLevel.Error && log.Message.Contains("system-directory.read", StringComparison.Ordinal));
        }
    }

    private sealed class FixedApi(HttpStatusCode status) : IIdentitySystemsApi
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage> GetListAsync(string culture, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://identity.test/api/v1/systems"),
                Content = status == HttpStatusCode.OK
                    ? JsonContent.Create(new[]
                    {
                        new { key = "identity", displayName = "Identity", homeUrl = "https://identity.test/", isCurrent = true },
                        new { key = "orders-browser", displayName = "Orders", homeUrl = "https://orders.test/", isCurrent = false }
                    })
                    : JsonContent.Create(new { title = "Forbidden", status = 403 })
            });
        }
    }
}
#endif
