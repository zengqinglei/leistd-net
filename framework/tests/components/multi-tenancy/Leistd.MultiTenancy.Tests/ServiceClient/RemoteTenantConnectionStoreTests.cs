using System.Net;
using System.Text;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.ServiceClient;
using Leistd.MultiTenancy.ServiceClient.Options;
using Leistd.MultiTenancy.ServiceClient.Stores;
using Leistd.ServiceClient.Exceptions;
using Leistd.TestBase.Doubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.ServiceClient;

/// <summary>
/// 远端连接存储：按控制面端点的路由回源，只把"租户不存在"翻成 null，其余错误一律上抛。
/// </summary>
/// <remarks>
/// 手写客户端曾只捕获 Refit 的 <c>ApiException</c>，而服务调用管道把非成功响应统一转成 <c>RemoteServiceException</c>，
/// "租户不存在返回 null"的分支永远不触发。这里用真实的错误形态断言。
/// </remarks>
public sealed class RemoteTenantConnectionStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly CapturingHttpMessageHandler _handler = new();

    [Fact]
    public async Task A_runtime_lookup_is_asked_by_name_and_mapped()
    {
        _handler.Responder = _ => Json(HttpStatusCode.OK, $$$"""
            {"tenantId":"{{{TenantId}}}","hasAnyConnection":true,"connection":{"name":"crm","connectionString":"Host=crm","version":3}}
            """);

        var lookup = await Store("/api/v1/tenant-connections/").FindAsync(TenantId, "crm");

        Assert.Equal($"/api/v1/tenant-connections/runtime/{TenantId}", _handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("?name=crm", _handler.Requests[0].RequestUri!.Query);
        Assert.Equal((TenantId, true, "crm", "Host=crm", 3L),
            (lookup!.TenantId, lookup.HasAnyConnection, lookup.Connection!.Name, lookup.Connection.ConnectionString, lookup.Connection.Version));
    }

    [Fact]
    public async Task A_missing_tenant_is_null()
    {
        _handler.Responder = _ => Json(HttpStatusCode.NotFound, """{"status":404,"code":"Tenant:NotFound"}""");

        Assert.Null(await Store().FindAsync(TenantId, "crm"));
    }

    // 路由配错也是 404：翻成 null 会表现成"所有租户都不存在"，必须大声失败
    [Fact]
    public async Task A_404_that_is_not_a_missing_tenant_is_thrown()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<RemoteServiceException>(() => Store().FindAsync(TenantId, "crm"));

        Assert.Equal(404, error.RemoteStatusCode);
    }

    // 控制面不可达不能表现成"这个租户不存在"
    [Fact]
    public async Task Server_errors_are_thrown()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<RemoteServiceException>(() => Store().FindAsync(TenantId, "crm"));
    }

    [Fact]
    public async Task Migration_targets_are_listed_by_name()
    {
        _handler.Responder = _ => Json(HttpStatusCode.OK, $$"""
            [{"tenantId":"{{TenantId}}","name":"default","connectionString":"Host=a"}]
            """);

        var targets = await Store().GetListAsync("crm");

        Assert.Equal("/api/v1/tenant-connections/migration", _handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(new TenantMigrationConnection(TenantId, "default", "Host=a"), Assert.Single(targets));
    }

    [Fact]
    public void Registering_it_next_to_the_control_database_store_fails()
    {
        var services = new ServiceCollection().AddMultiTenancyEfCore<DbContext>();

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddRemoteTenantConnectionStore("Identity", new ConfigurationBuilder().Build()));

        Assert.Contains("exactly one authoritative source", error.Message);
    }

    private RemoteTenantConnectionStore Store(string prefix = RemoteTenantConnectionClientOptions.DefaultRoutePrefix)
        => new(
            new HttpClient(_handler) { BaseAddress = new Uri("https://identity.test") },
            Microsoft.Extensions.Options.Options.Create(new RemoteTenantConnectionClientOptions { RoutePrefix = prefix }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };
}
