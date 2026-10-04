#if (RemoteTokenAuth && !IncludeMultiTenancy)
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Validation;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>真实 Bearer 验签后，宿主入口在投影和业务访问前拒绝租户声明。</summary>
public sealed class ResourceHostPrincipalTests
{
    private const string Issuer = "https://identity.test/";

    [Theory]
    [InlineData("tenant")]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("conflicting")]
    public async Task Invalid_scope_is_rejected_before_projection_for_api_and_hub(string kind)
    {
        using var factory = new ProjectWebApplicationFactory { UseProductionAuthentication = true };
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "host-scope-test" };
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:Audience", "resource-api")
            .ConfigureTestServices(services => services.Configure<OpenIddictValidationOptions>(options =>
            {
                options.Configuration = new OpenIddictConfiguration { Issuer = new Uri(Issuer) };
                options.Configuration.SigningKeys.Add(key);
            })));
        var subject = Guid.NewGuid();
        var tenant = Guid.NewGuid().ToString();
        object scope = kind switch
        {
            "tenant" => tenant,
            "malformed" => "not-a-guid",
            "empty" => "",
            "duplicate" => new[] { tenant, tenant },
            "conflicting" => new[] { tenant, Guid.NewGuid().ToString() },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var token = Token(subject, key, scope);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var api = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
#if (IncludeNotifications || IncludeRealTime)
#if (IncludeRealTime)
        const string hub = "/hubs/realtime/negotiate?negotiateVersion=1";
#else
        const string hub = "/hubs/notifications/negotiate?negotiateVersion=1";
#endif
        using var negotiation = await client.PostAsync(hub, null);
        Assert.Equal(HttpStatusCode.Unauthorized, negotiation.StatusCode);
#endif
        using var dbScope = host.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == subject));
        // 同一真实验签配置必须接受宿主，防止“全部拒绝”造成假绿。
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(subject, key, null));
        using var valid = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == subject));
    }

    private static string Token(Guid subject, SecurityKey key, object? tenant)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject.ToString(), ["jti"] = Guid.NewGuid().ToString() };
        if (tenant is not null) claims[CustomClaimTypes.TenantId] = tenant;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = "resource-api", Claims = claims, TokenType = "at+jwt",
            IssuedAt = DateTime.UtcNow, NotBefore = DateTime.UtcNow.AddSeconds(-5), Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });
    }
}
#endif
