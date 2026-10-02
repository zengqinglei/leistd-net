#if (OpenIddictServer)
using CompanyName.ProjectName.Client;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.OAuth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Client.SystemNetHttp;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ServiceInvocationTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Generated_client_explicit_machine_authentication_calls_anonymous_API_and_maps_natural_user_rejection()
    {
        var clientId = $"machine-{Guid.NewGuid():N}";
        const string secret = "ClientPackage!Secret1";
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId, ClientSecret = secret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + new OAuthOptions().Resource
                }
            });
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = factory.Services.GetRequiredService<IOptions<OAuthOptions>>().Value.Issuer ?? "https://localhost/",
            ["Leistd:ServiceAuth:ClientId"] = clientId,
            ["Leistd:ServiceAuth:ClientSecret"] = secret,
            ["Leistd:ServiceClients:MyProject:BaseAddress"] = "https://localhost/",
            ["Leistd:ServiceClients:MyProject:Scope"] = new OAuthOptions().Resource
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new ProtocolTransport(factory.Server));
        services.AddServiceAuthentication();
        services.AddMyProjectClient(configuration).AddClientCredentials()
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        await using var caller = services.BuildServiceProvider();
        var client = caller.GetRequiredService<IMyProjectClient>();
        var info = await client.GetServiceInfoAsync();
        Assert.False(string.IsNullOrWhiteSpace(info.Service));
        // 403 证明机器令牌已被认证，但不满足自然人策略；未认证调用会返回 401。
        var rejection = await Assert.ThrowsAsync<RemoteServiceException>(() => client.WhoAmIAsync());
        Assert.Equal(403, rejection.RemoteStatusCode);
        Assert.False(string.IsNullOrWhiteSpace(rejection.RemoteTraceId));
    }

    private sealed class ProtocolTransport(TestServer server) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (!builder.Name!.StartsWith(typeof(OpenIddictClientSystemNetHttpOptions).Assembly.GetName().Name!, StringComparison.Ordinal)) return;
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = server.CreateHandler();
        };
    }
}
#endif
