using Leistd.Authorization.DataScope;
using Leistd.Authorization.Resource;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.Pipeline.Tests;

public sealed record OrderDto(string ResourceKey, string Code, string OrganizationId, string OwnerId);

public sealed record PagedOrders(long TotalCount, IReadOnlyList<OrderDto> Items);

/// <summary>
/// 订单应用服务：按设计文档 §6.5 的标准执行链落地。
/// </summary>
/// <remarks>
/// 功能权限由端点上的 <c>[Authorize(Policy = ...)]</c> 前置把关，本服务负责其余两层：
/// 集合先经数据范围翻译成 SQL 谓词，单实例先加载再做资源判定。
/// </remarks>
public sealed class OrderAppService(
    PipelineDbContext dbContext,
    IDataScopeApplier dataScope,
    IResourceAuthorizationService resourceAuthorization,
    IPermissionSubjectProvider subjectProvider,
    IResourceGrantStore resourceGrantStore)
{
    /// <summary>
    /// 列表：先施加可见范围，再叠加分页。总数与当前页共用同一个范围入口。
    /// </summary>
    public async Task<PagedOrders> GetPagedListAsync(int offset, int limit, CancellationToken ct)
    {
        var scoped = await ScopedQueryAsync(DataOperations.Read, ct);

        var totalCount = await scoped.LongCountAsync(ct);
        var items = await scoped
            .OrderBy(order => order.Code)
            .Skip(offset)
            .Take(limit)
            .Select(order => new OrderDto(order.ResourceKey, order.Code, order.OrganizationId, order.OwnerId))
            .ToListAsync(ct);

        return new PagedOrders(totalCount, items);
    }

    /// <summary>
    /// 导出：必须与列表共用同一个范围入口，否则导出会多出用户看不到的数据。
    /// </summary>
    public async Task<IReadOnlyList<OrderDto>> ExportAsync(CancellationToken ct)
    {
        var scoped = await ScopedQueryAsync(DataOperations.Export, ct);
        return await scoped
            .OrderBy(order => order.Code)
            .Select(order => new OrderDto(order.ResourceKey, order.Code, order.OrganizationId, order.OwnerId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 详情：先在可见范围内定位（防水平越权），再对已加载实例做资源判定。
    /// </summary>
    public async Task<OrderDto?> GetAsync(string resourceKey, CancellationToken ct)
    {
        var scoped = await ScopedQueryAsync(DataOperations.Read, ct);
        var order = await scoped.SingleOrDefaultAsync(x => x.ResourceKey == resourceKey, ct);
        if (order == null)
        {
            // 范围外一律当作不存在：不泄漏"存在但你看不到"这一事实。
            return null;
        }

        return await resourceAuthorization.IsGrantedAsync(order, ResourceOperations.Read, ct)
            ? new OrderDto(order.ResourceKey, order.Code, order.OrganizationId, order.OwnerId)
            : null;
    }

    /// <summary>
    /// 更新：范围用 Update 而非 Read——能看不等于能改。
    /// </summary>
    public async Task<bool> UpdateAsync(string resourceKey, string code, CancellationToken ct)
    {
        var scoped = await ScopedQueryAsync(DataOperations.Update, ct);
        var order = await scoped.SingleOrDefaultAsync(x => x.ResourceKey == resourceKey, ct);
        if (order == null)
        {
            return false;
        }

        if (!await resourceAuthorization.IsGrantedAsync(order, ResourceOperations.Update, ct))
        {
            return false;
        }

        order.Code = code;
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// 批量：先在范围内定位目标，再核对数量，禁止静默跳过越权项。
    /// </summary>
    public async Task<bool> ArchiveManyAsync(IReadOnlyCollection<string> resourceKeys, CancellationToken ct)
    {
        var scoped = await ScopedQueryAsync(DataOperations.Update, ct);
        var targets = await scoped.Where(x => resourceKeys.Contains(x.ResourceKey)).ToListAsync(ct);

        if (targets.Count != resourceKeys.Count)
        {
            return false;
        }

        foreach (var order in targets)
        {
            order.IsArchived = true;
        }

        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// 分享给我的：ACL 作为集合入口合并进查询，而不是逐行判定。
    /// </summary>
    public async Task<IReadOnlyList<OrderDto>> GetSharedWithMeAsync(CancellationToken ct)
    {
        var subject = await subjectProvider.GetCurrentSubjectAsync(ct);
        if (subject == null)
        {
            return [];
        }

        var grantedKeys = resourceGrantStore.QueryGrantedResourceKeys(
            Order.Resource,
            ResourceOperations.Read,
            subject.UserId,
            subject.RoleIds);

        return await dbContext.Orders
            .Where(order => grantedKeys.Contains(order.ResourceKey))
            .OrderBy(order => order.Code)
            .Select(order => new OrderDto(order.ResourceKey, order.Code, order.OrganizationId, order.OwnerId))
            .ToListAsync(ct);
    }

    private async Task<IQueryable<Order>> ScopedQueryAsync(string operation, CancellationToken ct)
        => await dataScope.ApplyAsync(dbContext.Orders.AsQueryable(), Order.Resource, operation, ct);
}

public static class OrderEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 功能权限：权限名即策略名，由 PermissionPolicyProvider 动态构建。
        app.MapGet("/orders", async (OrderAppService service, CancellationToken ct) =>
                Results.Ok(await service.GetPagedListAsync(0, 50, ct)))
            .RequireAuthorization(OrderPermissions.Read);

        app.MapGet("/orders/export", async (OrderAppService service, CancellationToken ct) =>
                Results.Ok(await service.ExportAsync(ct)))
            .RequireAuthorization(OrderPermissions.Export);

        app.MapGet("/orders/shared", async (OrderAppService service, CancellationToken ct) =>
                Results.Ok(await service.GetSharedWithMeAsync(ct)))
            .RequireAuthorization(OrderPermissions.Read);

        app.MapGet("/orders/{key}", async (string key, OrderAppService service, CancellationToken ct) =>
            {
                var order = await service.GetAsync(key, ct);
                return order is null ? Results.NotFound() : Results.Ok(order);
            })
            .RequireAuthorization(OrderPermissions.Read);

        app.MapPut("/orders/{key}", async (
                string key,
                UpdateOrderRequest request,
                OrderAppService service,
                CancellationToken ct) =>
            {
                var updated = await service.UpdateAsync(key, request.Code, ct);
                return updated ? Results.NoContent() : Results.Forbid();
            })
            .RequireAuthorization(OrderPermissions.Update);

        app.MapPost("/orders/archive", async (
                ArchiveOrdersRequest request,
                OrderAppService service,
                CancellationToken ct) =>
            {
                var archived = await service.ArchiveManyAsync(request.ResourceKeys, ct);
                return archived ? Results.NoContent() : Results.Forbid();
            })
            .RequireAuthorization(OrderPermissions.Update);
    }
}

public sealed record UpdateOrderRequest(string Code);

public sealed record ArchiveOrdersRequest(IReadOnlyCollection<string> ResourceKeys);
