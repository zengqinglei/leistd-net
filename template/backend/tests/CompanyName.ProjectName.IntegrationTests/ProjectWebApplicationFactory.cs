using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ProjectWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"ProjectTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "",
                ["ConnectionStrings:Redis"] = "",
                ["Database:InMemoryName"] = databaseName,
                ["SpaProxy:Enabled"] = "false",
                ["OAuth:DisableHttpsRequirement"] = "true",
                ["DefaultAdmin:Username"] = "admin",
                ["DefaultAdmin:Password"] = "Admin@123456",
                ["UserRegistration:EnableEmailVerification"] = "false"
            });
        });

        // 测试宿主关闭确定化：强制托管服务顺序启停（关闭并发路径），避免 WebApplicationFactory 释放时
        // Host.ForeachService 与已释放的 CancellationTokenSource 竞争，导致类清理期偶发 ObjectDisposedException。
        // 作用于工厂 → 覆盖所有测试类（Health / Localization / Notifications 等），而非逐类修补。
        builder.ConfigureServices(services =>
        {
            services.Configure<HostOptions>(options =>
            {
                options.ServicesStartConcurrently = false;
                options.ServicesStopConcurrently = false;
            });
        });
    }

    public HttpClient CreateProjectClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

#if (IncludeIdentity)
    public async Task<AuthenticatedSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var client = CreateProjectClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = username, Password = password },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        Assert.False(string.IsNullOrWhiteSpace(cookie));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new AuthenticatedSession(client, cookie);
    }
#endif
}

public sealed class AuthenticatedSession(HttpClient client, string cookie) : IDisposable
{
    public HttpClient Client { get; } = client;
    public string Cookie { get; } = cookie;

    public void Dispose() => Client.Dispose();
}
