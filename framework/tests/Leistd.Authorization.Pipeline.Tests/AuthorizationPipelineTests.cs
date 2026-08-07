using System.Net;
using System.Net.Http.Json;
using Leistd.Authorization.Resource;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Authorization.Pipeline.Tests;

/// <summary>
/// 三层授权串起来之后的行为：功能权限 → 数据范围 → 资源实例授权。
/// </summary>
public class AuthorizationPipelineTests : IAsyncLifetime
{
    private PipelineHost _host = default!;

    private const string OwnOrder = "order-own";
    private const string ColleagueOrder = "order-colleague";
    private const string OutsideOrder = "order-outside";
    private const string ArchivedOrder = "order-archived";

    public async Task InitializeAsync()
    {
        _host = await PipelineHost.StartAsync();

        await _host.ExecuteAsync(async db =>
        {
            db.Orders.AddRange(
                new Order
                {
                    ResourceKey = OwnOrder, Code = "A-001",
                    OwnerId = PipelineFixtures.OwnerUserId, OrganizationId = PipelineFixtures.OrganizationId
                },
                new Order
                {
                    ResourceKey = ColleagueOrder, Code = "A-002",
                    OwnerId = PipelineFixtures.ColleagueUserId, OrganizationId = PipelineFixtures.OrganizationId
                },
                new Order
                {
                    ResourceKey = OutsideOrder, Code = "B-001",
                    OwnerId = PipelineFixtures.OutsiderUserId, OrganizationId = "org-2"
                },
                new Order
                {
                    ResourceKey = ArchivedOrder, Code = "A-003", IsArchived = true,
                    OwnerId = PipelineFixtures.OwnerUserId, OrganizationId = PipelineFixtures.OrganizationId
                });
            await db.SaveChangesAsync();
        });

        // 组织角色持有读取、更新与导出权限。
        await _host.GrantAsync(
            PermissionGrantProviderNames.Role,
            PipelineFixtures.OrgRoleId,
            OrderPermissions.Read,
            OrderPermissions.Update,
            OrderPermissions.Export);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Missing_functional_permission_is_rejected_before_any_data_is_touched()
    {
        // 没有任何授予的主体：第一层就应拦下，不该走到数据范围。
        var response = await _host.Client.SendAsync(
            _host.Request(HttpMethod.Get, "/orders", PipelineFixtures.OwnerUserId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    // 任一满足：持有其中任意一个权限都放行。
    [InlineData(OrderPermissions.Export)]
    [InlineData(OrderPermissions.Update)]
    public async Task Any_of_policy_accepts_each_listed_permission(string permission)
    {
        var userId = $"any-of-{permission}";
        await _host.GrantAsync(PermissionGrantProviderNames.User, userId, permission);

        var response = await _host.Client.SendAsync(
            _host.Request(HttpMethod.Get, "/orders/report", userId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Any_of_policy_still_rejects_a_subject_holding_none_of_them()
    {
        const string userId = "any-of-none";
        // 持有同一权限族里的其他权限，但不在策略列出的集合内。
        await _host.GrantAsync(PermissionGrantProviderNames.User, userId, OrderPermissions.Read);

        var response = await _host.Client.SendAsync(
            _host.Request(HttpMethod.Get, "/orders/report", userId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_is_challenged_rather_than_forbidden()
    {
        var response = await _host.Client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Data_scope_limits_the_list_to_the_assigned_organization()
    {
        var page = await GetAsync<PagedOrders>("/orders", PipelineFixtures.OwnerUserId);

        // 组织内 3 条可见，org-2 的那条不可见——总数与条目都必须一致。
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.DoesNotContain(page.Items, order => order.ResourceKey == OutsideOrder);
    }

    [Fact]
    public async Task Export_shares_the_same_scope_entry_as_the_list()
    {
        var page = await GetAsync<PagedOrders>("/orders", PipelineFixtures.OwnerUserId);
        var exported = await GetAsync<List<OrderDto>>("/orders/export", PipelineFixtures.OwnerUserId);

        // 导出多出一条就意味着用户能拿到列表里看不到的数据。
        Assert.Equal(page.TotalCount, exported.Count);
        Assert.DoesNotContain(exported, order => order.ResourceKey == OutsideOrder);
    }

    [Fact]
    public async Task Detail_outside_the_scope_is_indistinguishable_from_missing()
    {
        var response = await _host.Client.SendAsync(_host.Request(
            HttpMethod.Get, $"/orders/{OutsideOrder}", PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId));

        // 水平越权防护：范围外返回 404 而不是 403，避免泄漏资源存在性。
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_inside_the_scope_still_needs_the_resource_rule_to_allow()
    {
        // 同组织的同事订单在读取范围内，但所有者规则不放行 → 依然拿不到。
        var colleague = await _host.Client.SendAsync(_host.Request(
            HttpMethod.Get, $"/orders/{ColleagueOrder}", PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId));
        Assert.Equal(HttpStatusCode.NotFound, colleague.StatusCode);

        // 自己的订单：范围内且规则放行。
        var own = await GetAsync<OrderDto>($"/orders/{OwnOrder}", PipelineFixtures.OwnerUserId);
        Assert.Equal("A-001", own.Code);
    }

    [Fact]
    public async Task Read_scope_does_not_imply_update_scope()
    {
        // 读取范围是整个组织，更新范围只有本人：同事的订单看得到、改不了。
        var response = await _host.Client.SendAsync(WithBody(
            _host.Request(HttpMethod.Put, $"/orders/{ColleagueOrder}",
                PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId),
            new UpdateOrderRequest("HACKED")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await _host.ExecuteAsync(async db =>
        {
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.ResourceKey == ColleagueOrder);
            Assert.Equal("A-002", order.Code);
        });
    }

    [Fact]
    public async Task Own_order_can_be_updated_through_the_full_chain()
    {
        var response = await _host.Client.SendAsync(WithBody(
            _host.Request(HttpMethod.Put, $"/orders/{OwnOrder}",
                PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId),
            new UpdateOrderRequest("A-001-UPDATED")));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await _host.ExecuteAsync(async db =>
        {
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.ResourceKey == OwnOrder);
            Assert.Equal("A-001-UPDATED", order.Code);
        });
    }

    [Fact]
    public async Task Domain_rule_denial_beats_scope_and_ownership()
    {
        // 已归档订单：在更新范围内、也是本人所有，但领域规则拒绝——拒绝优先。
        var response = await _host.Client.SendAsync(WithBody(
            _host.Request(HttpMethod.Put, $"/orders/{ArchivedOrder}",
                PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId),
            new UpdateOrderRequest("SHOULD-NOT-APPLY")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Batch_operation_rejects_the_whole_request_instead_of_skipping_denied_items()
    {
        var response = await _host.Client.SendAsync(WithBody(
            _host.Request(HttpMethod.Post, "/orders/archive",
                PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId),
            new ArchiveOrdersRequest([OwnOrder, ColleagueOrder])));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 静默跳过越权项会让调用方以为整批都成功了，因此本人的那条也不能被处理。
        await _host.ExecuteAsync(async db =>
        {
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.ResourceKey == OwnOrder);
            Assert.False(order.IsArchived);
        });
    }

    [Fact]
    public async Task Acl_grant_is_merged_into_the_collection_query()
    {
        // 组织外的订单通过 ACL 分享给我：集合查询里应当出现，且不依赖数据范围。
        await _host.ReplaceResourceGrantsAsync(
            OutsideOrder,
            new ResourceGrant(
                ResourceOperations.Read,
                PermissionGrantProviderNames.User,
                PipelineFixtures.OwnerUserId,
                PermissionGrantEffect.Granted));

        var shared = await GetAsync<List<OrderDto>>("/orders/shared", PipelineFixtures.OwnerUserId);

        var order = Assert.Single(shared);
        Assert.Equal(OutsideOrder, order.ResourceKey);
    }

    [Fact]
    public async Task Acl_denial_removes_the_resource_from_the_collection_query()
    {
        await _host.ReplaceResourceGrantsAsync(
            OutsideOrder,
            new ResourceGrant(
                ResourceOperations.Read,
                PermissionGrantProviderNames.Role,
                PipelineFixtures.OrgRoleId,
                PermissionGrantEffect.Granted),
            new ResourceGrant(
                ResourceOperations.Read,
                PermissionGrantProviderNames.User,
                PipelineFixtures.OwnerUserId,
                PermissionGrantEffect.Prohibited));

        var shared = await GetAsync<List<OrderDto>>("/orders/shared", PipelineFixtures.OwnerUserId);

        Assert.Empty(shared);
    }

    [Fact]
    public async Task Revoking_the_functional_permission_takes_effect_on_the_next_request()
    {
        var before = await _host.Client.SendAsync(_host.Request(
            HttpMethod.Get, "/orders", PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
            await manager.ReplaceGrantsAsync(
                PermissionGrantProviderNames.Role,
                PipelineFixtures.OrgRoleId,
                []);
        }

        var after = await _host.Client.SendAsync(_host.Request(
            HttpMethod.Get, "/orders", PipelineFixtures.OwnerUserId, PipelineFixtures.OrgRoleId));
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    private async Task<T> GetAsync<T>(string url, string userId)
    {
        var response = await _host.Client.SendAsync(
            _host.Request(HttpMethod.Get, url, userId, PipelineFixtures.OrgRoleId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static HttpRequestMessage WithBody<T>(HttpRequestMessage request, T body)
    {
        request.Content = JsonContent.Create(body);
        return request;
    }
}
