using System.Net;
#if (IncludeMultiTenancy && LocalIdentity)
using System.Net.Http.Json;
using System.Text.Json;
#endif
using Leistd.MultiTenancy.Context;
#if (IncludeMultiTenancy && LocalIdentity)
using Leistd.MultiTenancy.AspNetCore.Options;
#endif
using Leistd.Tracing.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>请求完成日志：每个请求一条官方 HTTP 日志，带关联标识与本请求的租户；关闭时只保留 5xx。</summary>
/// <remarks>
/// 按响应头里的关联标识找回该请求的记录，计数精确到单个请求。探针端点只在本类的派生宿主里登记，
/// 状态码与异常由测试决定，不依赖业务端点的行为。
/// </remarks>
public sealed class RequestLoggingTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Category = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";

    [Fact]
    public async Task A_completed_request_is_one_combined_record_with_minimal_fields_and_scopes()
    {
        using var host = Host(enabled: true);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        using var response = await client.GetAsync($"{RequestLoggingProbeController.BasePath}/200?token=query-secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = Assert.Single(Records(host, response));
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("GET", LogRecordFields.State(record, "Method"));
        Assert.Equal($"/{RequestLoggingProbeController.BasePath}/200", LogRecordFields.State(record, "Path"));
        Assert.Equal("200", LogRecordFields.State(record, "StatusCode"));
        Assert.NotNull(LogRecordFields.State(record, "Duration"));
        // 不记查询串、请求头与正文
        Assert.DoesNotContain(record.StructuredState!, pair => pair.Value?.Contains("query-secret", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("query-secret", record.Message, StringComparison.Ordinal);
        // 宿主请求：租户字段存在，值为空
        Assert.True(LogRecordFields.HasState(record, TenantLogKeys.TenantId));
        Assert.Null(LogRecordFields.State(record, TenantLogKeys.TenantId));
    }

    /// <summary>关闭后正常完成与客户端错误都不记，5xx 照常记一条。</summary>
    [Theory]
    [InlineData("200", HttpStatusCode.OK, 0)]
    [InlineData("409", HttpStatusCode.Conflict, 0)]
    [InlineData("204", HttpStatusCode.NoContent, 0)]
    [InlineData("fail", HttpStatusCode.InternalServerError, 1)]
    public async Task Disabled_request_logging_keeps_only_server_errors(string probe, HttpStatusCode status, int expected)
    {
        using var host = Host(enabled: false);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        using var response = await client.GetAsync($"{RequestLoggingProbeController.BasePath}/{probe}");

        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.Conflict)
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(expected, Records(host, response).Length);
    }

    /// <summary>开启时同一组请求各记一条：上一条用例的 0 不是因为请求没进管道。</summary>
    [Theory]
    [InlineData("200", HttpStatusCode.OK)]
    [InlineData("409", HttpStatusCode.Conflict)]
    [InlineData("204", HttpStatusCode.NoContent)]
    [InlineData("fail", HttpStatusCode.InternalServerError)]
    public async Task Enabled_request_logging_records_every_request(string probe, HttpStatusCode status)
    {
        using var host = Host(enabled: true);
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        using var response = await client.GetAsync($"{RequestLoggingProbeController.BasePath}/{probe}");

        Assert.Equal(status, response.StatusCode);
        var record = Assert.Single(Records(host, response));
        Assert.Equal(((int)status).ToString(), LogRecordFields.State(record, "StatusCode"));
    }
#if (IncludeMultiTenancy)

    /// <summary>租户请求的完成日志带本请求的租户：成功、无响应体与异常转成的 5xx 都一样。</summary>
    /// <remarks>完成日志在租户中间件退出之后才写，这里证明租户不是从那时的 <c>ICurrentTenant</c> 读的。</remarks>
    [Theory]
    [InlineData("200", HttpStatusCode.OK)]
    [InlineData("204", HttpStatusCode.NoContent)]
    [InlineData("fail", HttpStatusCode.InternalServerError)]
    public async Task Tenant_requests_carry_their_tenant(string probe, HttpStatusCode status)
    {
        using var host = Host(enabled: true);
        var (client, tenantId) = await TenantClientAsync(host);
        using (client)
        {
            using var response = await client.GetAsync($"{RequestLoggingProbeController.BasePath}/{probe}");

            Assert.Equal(status, response.StatusCode);
            var record = Assert.Single(Records(host, response));
            Assert.Equal(tenantId.ToString(), LogRecordFields.State(record, TenantLogKeys.TenantId));
        }
    }
#endif

    private WebApplicationFactory<Program> Host(bool enabled) => factory.WithWebHostBuilder(builder => builder
        .UseSetting("RequestLogging:Enabled", enabled ? "true" : "false")
        .ConfigureServices(services => services.AddControllers()
            .AddApplicationPart(typeof(RequestLoggingProbeController).Assembly)));

    private static FakeLogRecord[] Records(WebApplicationFactory<Program> host, HttpResponseMessage response)
    {
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdConstants.DefaultHeaderName));
        return host.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Category == Category
                && LogRecordFields.Scope(record, CorrelationIdConstants.LogKey) == correlationId)
            .ToArray();
    }
#if (IncludeMultiTenancy && LocalIdentity)

    // 匿名请求带租户提示头即进入该租户；Identity 校验租户已登记，因此先建一个
    private static async Task<(HttpClient Client, Guid TenantId)> TenantClientAsync(WebApplicationFactory<Program> host)
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = $"reqlog-{Guid.NewGuid():N}"[..20];
        using var created = await admin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = $"{name} Inc.",
            AdminEmail = $"admin@{name}.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();

        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        return (client, tenantId);
    }
#elif (IncludeMultiTenancy)

    // 资源服务只信已验证令牌里的租户声明
    private static Task<(HttpClient Client, Guid TenantId)> TenantClientAsync(WebApplicationFactory<Program> host)
    {
        var tenantId = Guid.CreateVersion7();
        var session = ProjectWebApplicationFactory.CreateResourceSession(host, Guid.CreateVersion7(), tenantId);
        return Task.FromResult((session.Client, tenantId));
    }
#endif
}

/// <summary>请求日志用例的探针端点：按路由返回指定状态码，<c>fail</c> 抛出未处理异常（由全局异常处理转成 500）。</summary>
[ApiController]
[Route(BasePath)]
public sealed class RequestLoggingProbeController : ControllerBase
{
    public const string BasePath = "api/__tests/request-logging";

    [HttpGet("200")]
    public IActionResult Ok200() => Ok(new { probe = true });

    [HttpGet("204")]
    public IActionResult NoContent204() => NoContent();

    [HttpGet("409")]
    public IActionResult Conflict409() => Conflict(new { reason = "probe" });

    [HttpGet("fail")]
    public IActionResult Fail() => throw new InvalidOperationException("Request logging probe failure.");
}
