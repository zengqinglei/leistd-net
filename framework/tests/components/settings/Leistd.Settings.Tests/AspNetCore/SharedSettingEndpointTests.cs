using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Leistd.Lock.Memory;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Stores;
using Leistd.Security.AspNetCore;
using Leistd.Settings.AspNetCore.Endpoints;
using Leistd.Settings.Definitions;
using Leistd.Settings.Dtos;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Stores;
using Leistd.Settings.Tests.TestDoubles;
using Leistd.UnitOfWork;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Settings.Tests.AspNetCore;

/// <summary>
/// 共享设置端点：其他服务以机器身份按租户读取。只下发标记共享的设置，值为该租户层的值、没有时为代码默认值，
/// 带强 ETag；租户在路径里显式给出，无效租户 404。
/// </summary>
public sealed class SharedSettingEndpointTests : IAsyncLifetime
{
    private const string Policy = "App.SharedSettings.Read";
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Inactive = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly CurrentTenant Tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly TenantStore _store = new(Tenant);
    private readonly RecordingUnitOfWorkManager _unitOfWork = new();
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
                    services.AddSingleton<ICurrentTenant>(Tenant);
                    services.AddSingleton<ITenantStore>(new Tenants());
                    services.AddMemoryLocalLock();
                    services.AddSingleton<IUnitOfWorkManager>(_unitOfWork);
                    services.AddSingleton<ISettingDefinitionProvider, Definitions>();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, MachineHandler>("Test", _ => { });
                    services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireClaim("scope", "shared-settings.read"));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1/internal/shared-settings")
                        .MapSharedSettings(options => options.Policy = Policy));
                }))
            .StartAsync();
        _client = _host.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Scope", "shared-settings.read");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public void Mapping_without_a_policy_fails()
    {
        var error = Assert.Throws<ArgumentException>(() => new FakeEndpointRouteBuilder().MapSharedSettings(_ => { }));

        Assert.Equal(nameof(SharedSettingEndpointOptions.Policy), error.ParamName);
    }

    /// <summary>只下发共享的设置；租户层有值用它，没有用代码默认值，不读个人覆盖。读取在目标租户下、新开的工作单元里进行。</summary>
    [Fact]
    public async Task A_tenant_gets_its_effective_shared_values()
    {
        _store.Rows[TenantA] = new() { ["Security.StepUp.WindowMinutes"] = "30", ["Export.MaxRowsPerFile"] = "100" };

        var response = await _client.GetAsync($"/api/v1/internal/shared-settings/{TenantA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SharedSettingsDto>();
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Display.Language"] = null,
                ["Security.StepUp.Enabled"] = "true",
                ["Security.StepUp.WindowMinutes"] = "30",
            },
            body!.Values);
        Assert.Equal([TenantA], _store.ReadTenants);
        Assert.True(Assert.Single(_unitOfWork.Begun).RequiresNew);
        Assert.False(_store.UserRead);
        Assert.Null(Tenant.Id);
    }

    [Fact]
    public async Task The_host_segment_reads_the_host_values()
    {
        _store.Rows[Guid.Empty] = new() { ["Security.StepUp.Enabled"] = "false" };

        var body = await _client.GetFromJsonAsync<SharedSettingsDto>($"/api/v1/internal/shared-settings/{SharedSettingsDto.HostSegment}");

        Assert.Equal("false", body!.Values["Security.StepUp.Enabled"]);
        Assert.Equal([null], _store.ReadTenants);
    }

    /// <summary>强 ETag：同一组值同一个标签，<c>If-None-Match</c> 命中时 304 不带响应体；值变了标签跟着变。</summary>
    [Fact]
    public async Task A_matching_etag_is_answered_with_not_modified()
    {
        var first = await _client.GetAsync($"/api/v1/internal/shared-settings/{TenantA}");
        var etag = first.Headers.ETag!;
        Assert.False(etag.IsWeak);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/internal/shared-settings/{TenantA}");
        request.Headers.IfNoneMatch.Add(etag);
        var revalidated = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
        Assert.Empty(await revalidated.Content.ReadAsByteArrayAsync());

        _store.Rows[TenantA] = new() { ["Security.StepUp.Enabled"] = "false" };
        request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/internal/shared-settings/{TenantA}");
        request.Headers.IfNoneMatch.Add(etag);
        var changed = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("account-preferences.read", HttpStatusCode.Forbidden)]
    public async Task Callers_without_the_policy_are_rejected(string? scope, HttpStatusCode expected)
    {
        using var client = _host.GetTestClient();
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add("X-Scope", scope);
        }

        var response = await client.GetAsync($"/api/v1/internal/shared-settings/{TenantA}");

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(_store.ReadTenants);
    }

    /// <summary>不是 <c>host</c> 也不是存在且启用的租户：一律 404，不读设置。</summary>
    [Theory]
    [InlineData("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [InlineData("cccccccc-cccc-cccc-cccc-cccccccccccc")]
    [InlineData("not-a-tenant")]
    [InlineData("HOST")]
    public async Task An_unknown_or_inactive_tenant_is_not_found(string tenant)
    {
        var response = await _client.GetAsync($"/api/v1/internal/shared-settings/{tenant}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_store.ReadTenants);
    }

    private sealed class Definitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Security.StepUp.Enabled", "true").AsBoolean().IsShared = true;
            context.Add("Security.StepUp.WindowMinutes", "15").AsInteger(1, 60).IsShared = true;
            // 组织给默认、个人可覆盖：共享的是租户层，不含个人覆盖
            context.Add("Display.Language", scopes: SettingScopes.All).IsShared = true;
            context.Add("Export.MaxRowsPerFile", "50000");
        }
    }

    private sealed class Tenants : ITenantStore
    {
        public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == TenantA ? new TenantConfiguration { Id = id, Name = "a", NormalizedName = "A" }
                : id == Inactive ? new TenantConfiguration { Id = id, Name = "inactive", NormalizedName = "INACTIVE", IsActive = false }
                : null);

        public Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantConfiguration?>(null);
    }

    // 按调用时的租户分开存放；宿主那份放在 Guid.Empty 下。记录每次租户层读取发生在哪个租户
    private sealed class TenantStore(ICurrentTenant currentTenant) : ISettingStore
    {
        public Dictionary<Guid, Dictionary<string, string>> Rows { get; } = [];

        public List<Guid?> ReadTenants { get; } = [];

        public bool UserRead { get; private set; }

        public bool CanAccessHostScope => !currentTenant.IsAvailable;

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            SettingScopes scope, string? userId, CancellationToken cancellationToken = default)
        {
            if (scope == SettingScopes.Host)
            {
                return Task.FromException<IReadOnlyDictionary<string, string>>(new HostScopeUnavailableException());
            }

            if (scope == SettingScopes.User)
            {
                UserRead = true;
            }
            else
            {
                ReadTenants.Add(currentTenant.Id);
            }

            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                scope == SettingScopes.Tenant && Rows.TryGetValue(currentTenant.Id ?? Guid.Empty, out var values)
                    ? values
                    : new Dictionary<string, string>());
        }

        public Task RemoveAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetAsync(string name, string? value, SettingScopes scope, string? userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // 机器主体：带 X-Scope 头即认证成功，scope 声明照抄
    private sealed class MachineHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Scope", out var scope))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", "client:svc"), new Claim("scope", scope.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class FakeEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public IApplicationBuilder CreateApplicationBuilder() => throw new NotSupportedException();

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
    }
}
