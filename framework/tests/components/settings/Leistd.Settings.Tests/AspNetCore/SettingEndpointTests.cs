using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Leistd.ExceptionHandling;
using Leistd.Security.AspNetCore;
using Leistd.Security.Claims;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.AspNetCore.Endpoints;
using Leistd.Settings.Dtos;
using Leistd.Settings.Tests.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Settings.Tests.AspNetCore;

/// <summary>
/// 设置端点：两条策略名都由宿主给，自用端点走 <c>AccessPolicy</c>、改租户值走 <c>TenantWritePolicy</c>；
/// 取值错误是带码的 400。
/// </summary>
public sealed class SettingEndpointTests : IAsyncLifetime
{
    private const string AccessPolicy = "App.CurrentUser";
    private const string TenantWritePolicy = "App.Settings";
    private static readonly Guid UserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private readonly FakeSettingStore _store = new();
    private IHost _host = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSecurity();
                    services.AddSettingsCore();
                    services.AddSingleton<ISettingStore>(_store);
                    services.AddSingleton<ISettingDefinitionProvider, Definitions>();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, SubjectHandler>("Test", _ => { });
                    services.AddAuthorizationBuilder()
                        .AddPolicy(AccessPolicy, policy => policy.RequireAuthenticatedUser())
                        .AddPolicy(TenantWritePolicy, policy => policy.RequireClaim("permission", TenantWritePolicy));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    // 把业务异常换成状态码与错误码头，只为断言；宿主里由异常处理组件输出 Problem Details
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        catch (BusinessException exception)
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            context.Response.Headers["X-Error-Code"] = exception.Code;
                        }
                    });
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1/settings")
                        .MapSettings(options =>
                        {
                            options.AccessPolicy = AccessPolicy;
                            options.TenantWritePolicy = TenantWritePolicy;
                        }));
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

    [Theory]
    [InlineData(nameof(SettingEndpointOptions.AccessPolicy))]
    [InlineData(nameof(SettingEndpointOptions.TenantWritePolicy))]
    public void Mapping_without_a_policy_name_fails(string missing)
    {
        var endpoints = new FakeEndpointRouteBuilder();

        var error = Assert.Throws<ArgumentException>(() => endpoints.MapSettings(options =>
        {
            if (missing != nameof(SettingEndpointOptions.AccessPolicy))
            {
                options.AccessPolicy = AccessPolicy;
            }
        }));

        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "", null)).StatusCode);
    }

    [Fact]
    public async Task Any_user_reads_and_changes_their_own_preferences()
    {
        var list = await SendAsync(HttpMethod.Get, "", UserId.ToString());
        var write = await SendAsync(HttpMethod.Put, "/current-user", UserId.ToString(), new SetSettingInputDto("Display.TimeZone", "Asia/Tokyo"));

        Assert.Contains(await list.Content.ReadFromJsonAsync<SettingOutputDto[]>() ?? [], s => s.Name == "Display.TimeZone");
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.Equal(("Display.TimeZone", SettingScopes.User, UserId.ToString()), (_store.Writes[0].Name, _store.Writes[0].Scope, _store.Writes[0].UserId));
    }

    [Fact]
    public async Task Tenant_writes_need_the_configured_policy()
    {
        var input = new SetSettingInputDto("Security.RequireTwoFactor", "true");

        var denied = await SendAsync(HttpMethod.Put, "/current-tenant", UserId.ToString(), input);
        var granted = await SendAsync(HttpMethod.Put, "/current-tenant", UserId.ToString(), input, TenantWritePolicy);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        Assert.Equal(SettingScopes.Tenant, Assert.Single(_store.Writes).Scope);
    }

    [Fact]
    public async Task An_invalid_value_is_a_coded_bad_request()
    {
        var response = await SendAsync(
            HttpMethod.Put, "/current-tenant", UserId.ToString(), new SetSettingInputDto("Security.RequireTwoFactor", "yes"), TenantWritePolicy);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SettingErrorCodes.BooleanRequired, response.Headers.GetValues("X-Error-Code").Single());
        Assert.Empty(_store.Writes);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? subject,
        SetSettingInputDto? body = null,
        string? permission = null)
    {
        var request = new HttpRequestMessage(method, "/api/v1/settings" + path);
        if (subject is not null)
        {
            request.Headers.Add("X-Subject", subject);
        }

        if (permission is not null)
        {
            request.Headers.Add("X-Permission", permission);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    private sealed class Definitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Display.TimeZone", scopes: SettingScopes.All).IsVisibleToClients = true;
            context.Add("Security.RequireTwoFactor", "false").AsBoolean().IsVisibleToClients = true;
        }
    }

    private sealed class FakeEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    // X-Subject 决定主体的 sub，X-Permission 给一个权限声明；没有 X-Subject 即匿名
    private sealed class SubjectHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Subject", out var subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new(CustomClaimTypes.Subject, subject.ToString())];
            if (Request.Headers.TryGetValue("X-Permission", out var permission))
            {
                claims.Add(new Claim("permission", permission.ToString()));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
