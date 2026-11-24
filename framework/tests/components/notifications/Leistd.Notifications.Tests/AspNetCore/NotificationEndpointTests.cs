using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Leistd.Data.Paging;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Stores;
using Leistd.Notifications.AspNetCore.Endpoints;
using Leistd.Notifications.Dtos;
using Leistd.Security.AspNetCore;
using Leistd.Security.Claims;
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

namespace Leistd.Notifications.Tests.AspNetCore;

/// <summary>通知中心端点：走宿主给的具名策略、只作用于当前用户、列表按偏移分页且条数有上限。</summary>
/// <remarks>收件人若可由参数指定，任何登录用户都能读、删别人的通知；条数没有上限时一次请求能把整张表拉回来。</remarks>
public sealed class NotificationEndpointTests : IAsyncLifetime
{
    private const string AccessPolicy = "App.CurrentUser";
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly CapturingStore _store = new();
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
                    services.AddNotifications();
                    services.AddGlobalExceptionHandler();
                    services.AddSingleton<INotificationStore>(_store);
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, SubjectHandler>("Test", _ => { });
                    services.AddAuthorizationBuilder()
                        .AddPolicy(AccessPolicy, policy => policy.RequireAuthenticatedUser());
                })
                .Configure(app =>
                {
                    app.UseGlobalExceptionHandler();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1/notifications")
                        .MapNotifications(options => options.AccessPolicy = AccessPolicy));
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

    /// <summary>漏配策略名在映射时就失败，而不是留一个"谁都能进"的通知中心。</summary>
    [Fact]
    public void Mapping_without_an_access_policy_fails()
    {
        var services = new ServiceCollection().AddRouting().AddSecurity().BuildServiceProvider();
        var endpoints = new FakeEndpointRouteBuilder(services);

        var error = Assert.Throws<ArgumentException>(() => endpoints.MapNotifications(_ => { }));

        Assert.Contains(nameof(NotificationEndpointOptions.AccessPolicy), error.Message);
    }

    private sealed class FakeEndpointRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? subject)
    {
        var request = new HttpRequestMessage(method, url);
        if (subject is not null)
        {
            request.Headers.Add("X-Subject", subject);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/notifications", null)).StatusCode);
    }

    /// <summary>收件人取自当前主体；偏移、条数与未读过滤原样交给存储，总数与当前页一并返回。</summary>
    [Theory]
    [InlineData("", 0, GetNotificationsInputDto.DefaultLimit, false)]
    [InlineData("?offset=20&limit=100&unreadOnly=true", 20, GetNotificationsInputDto.MaximumLimit, true)]
    [InlineData("?limit=1", 0, 1, false)]
    public async Task The_list_belongs_to_the_current_user_and_pages_by_offset_and_limit(
        string query, int offset, int limit, bool unreadOnly)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/notifications" + query, UserId.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((UserId.ToString(), offset, limit, unreadOnly), _store.LastQuery);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CapturingStore.TotalCount, page.GetProperty("totalCount").GetInt64());
        Assert.Equal("n1", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
    }

    /// <summary>越界不再静默收敛：400 字段错误、不带业务码，也不查询存储。</summary>
    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?offset=-1", "offset")]
    public async Task Out_of_range_paging_is_400_with_the_field(string query, string field)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/notifications" + query, UserId.ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        Assert.Equal(field, Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).GetProperty("field").GetString());
        Assert.Null(_store.LastQuery);
    }

    /// <summary>当前身份不是用户（机器客户端）：读取返回空页，写入以带码的 403 拒绝。</summary>
    [Fact]
    public async Task A_non_user_identity_reads_nothing_and_cannot_modify()
    {
        var list = await SendAsync(HttpMethod.Get, "/api/v1/notifications", "client:reporting");
        var clear = await SendAsync(HttpMethod.Delete, "/api/v1/notifications", "client:reporting");

        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, page.GetProperty("totalCount").GetInt64());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Null(_store.LastQuery);
        Assert.Equal(HttpStatusCode.Forbidden, clear.StatusCode);
        using var problem = JsonDocument.Parse(await clear.Content.ReadAsStringAsync());
        Assert.Equal(NotificationErrorCodes.IdentityCannotOperate, problem.RootElement.GetProperty("code").GetString());
        Assert.Null(_store.LastDeletedUser);
    }

    [Theory]
    [InlineData("PUT", "/api/v1/notifications/n1/read")]
    [InlineData("PUT", "/api/v1/notifications/read-all")]
    [InlineData("DELETE", "/api/v1/notifications/n1")]
    [InlineData("DELETE", "/api/v1/notifications")]
    public async Task Modifications_act_on_the_current_user(string method, string url)
    {
        var response = await SendAsync(new HttpMethod(method), url, UserId.ToString());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(UserId.ToString(), _store.LastModifiedUser);
    }

    private sealed class CapturingStore : INotificationStore
    {
        public const long TotalCount = 7;

        public (string UserId, int Offset, int Limit, bool UnreadOnly)? LastQuery { get; private set; }
        public string? LastModifiedUser { get; private set; }
        public string? LastDeletedUser { get; private set; }

        public Task SaveAsync(NotificationOutputDto notification, string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PagedResult<NotificationOutputDto>> GetByUserAsync(string userId, PageRequest page, bool unreadOnly = false, CancellationToken ct = default)
        {
            LastQuery = (userId, page.Offset, page.Limit, unreadOnly);
            return Task.FromResult(new PagedResult<NotificationOutputDto>(TotalCount,
                [new NotificationOutputDto { Id = "n1", Title = "n1", CreationTime = DateTime.UtcNow }]));
        }

        public Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default) => Record(userId);
        public Task MarkAllAsReadAsync(string userId, CancellationToken ct = default) => Record(userId);
        public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default) => Task.FromResult(1);

        public async Task<bool> DeleteAsync(string notificationId, string userId, CancellationToken ct = default)
        {
            await Record(userId);
            return true;
        }

        public async Task<int> DeleteAllAsync(string userId, CancellationToken ct = default)
        {
            await Record(userId);
            LastDeletedUser = userId;
            return 1;
        }

        private Task Record(string userId)
        {
            LastModifiedUser = userId;
            return Task.CompletedTask;
        }
    }

    // X-Subject 头决定主体的 sub；没有该头即匿名
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

            var identity = new ClaimsIdentity([new Claim(CustomClaimTypes.Subject, subject.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
