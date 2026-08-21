using System.Security.Claims;
using Leistd.Exception.Core;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

public class AuthenticatedTenantContextMiddlewareTests : IAsyncLifetime
{
    private IHost _host = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services => services.AddMultiTenancy())
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Auth", out var subject))
                        {
                            var claims = new List<Claim> { new("sub", subject.ToString()) };
                            foreach (var value in context.Request.Headers["X-Test-Tenant-Claim"])
                            {
                                claims.Add(new Claim(CustomClaimTypes.TenantId, value!));
                            }

                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
                        }

                        await next(context);
                    });

                    app.Use((context, next) =>
                    {
                        if (context.Request.Path == "/anonymous")
                        {
                            context.SetEndpoint(new Endpoint(
                                _ => Task.CompletedTask,
                                new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                                "anonymous"));
                        }

                        return next(context);
                    });

                    app.UseAuthenticatedTenantContext();
                    app.Run(async context =>
                    {
                        var tenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
                        await context.Response.WriteAsync(tenant.Id?.ToString() ?? "host");
                    });
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Unique_valid_claim_establishes_tenant_without_a_store()
    {
        var tenantId = Guid.NewGuid();

        Assert.Equal(tenantId.ToString(), await SendAsync(
            ("X-Test-Auth", "user"),
            ("X-Test-Tenant-Claim", tenantId.ToString())));
    }

    [Fact]
    public async Task Header_query_and_domain_hints_cannot_override_verified_claim()
    {
        var tenantId = Guid.NewGuid();

        var body = await SendAsync(
            [
                ("Host", "attacker.example.test"),
                ("X-Test-Auth", "user"),
                ("X-Test-Tenant-Claim", tenantId.ToString()),
                ("X-Tenant-Id", Guid.NewGuid().ToString())
            ],
            $"/?tenant={Guid.NewGuid()}");

        Assert.Equal(tenantId.ToString(), body);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => SendAsync());
    }

    [Fact]
    public async Task Anonymous_endpoint_does_not_require_a_tenant_principal()
    {
        Assert.Equal("host", await SendAsync(path: "/anonymous"));
    }

    [Fact]
    public async Task Missing_tenant_claim_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => SendAsync(("X-Test-Auth", "user")));
    }

    [Fact]
    public async Task Invalid_tenant_claim_is_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => SendAsync(
            ("X-Test-Auth", "user"),
            ("X-Test-Tenant-Claim", "not-a-guid")));
    }

    [Fact]
    public async Task Duplicate_tenant_claims_are_rejected_even_when_values_match()
    {
        var tenantId = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<UnauthorizedException>(() => SendAsync(
            ("X-Test-Auth", "user"),
            ("X-Test-Tenant-Claim", tenantId),
            ("X-Test-Tenant-Claim", tenantId)));
    }

    private async Task<string> SendAsync(
        (string Name, string Value)[]? headers = null,
        string path = "/")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await _client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private Task<string> SendAsync(params (string Name, string Value)[] headers) => SendAsync(headers, "/");
}
