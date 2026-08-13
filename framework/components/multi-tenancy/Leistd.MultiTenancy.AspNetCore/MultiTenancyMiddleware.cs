using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy;

/// <summary>
/// 多租户中间件：解析 → 校验 → 以租户上下文包裹后续管道
/// </summary>
/// <remarks>
/// <para>放置顺序：<c>UseAuthentication()</c>（及 <c>UseServiceUserContext()</c>）之后、<c>UseAuthorization()</c> 之前——
/// Claim 贡献者需要已认证主体，而权限检查必须在租户上下文内执行。</para>
/// <para>失败语义：解析出的租户不存在抛 <see cref="TenantNotFoundException"/>（404），
/// 已停用抛 <see cref="TenantNotActiveException"/>（403），由全局异常处理器统一映射；
/// 未解析出租户不是错误，即宿主上下文。</para>
/// </remarks>
public class MultiTenancyMiddleware(RequestDelegate next, ILogger<MultiTenancyMiddleware> logger)
{
    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var resolver = context.RequestServices.GetRequiredService<ITenantResolver>();
        var result = await resolver.ResolveAsync();

        if (result.TenantIdOrName is null)
        {
            // 宿主上下文，不做任何切换
            await next(context);
            return;
        }

        var tenant = await FindTenantAsync(context, result.TenantIdOrName);
        if (tenant is null)
        {
            throw new TenantNotFoundException(result.TenantIdOrName);
        }

        if (!tenant.IsActive)
        {
            throw new TenantNotActiveException(result.TenantIdOrName);
        }

        var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();

        // Change 包裹整个下游管道，使 AsyncLocal 租户上下文覆盖全请求
        using (currentTenant.Change(tenant.Id, tenant.Name))
        using (logger.BeginScope(new Dictionary<string, object> { ["leistd.tenantId"] = tenant.Id }))
        {
            await next(context);
        }
    }

    private static async Task<TenantConfiguration?> FindTenantAsync(HttpContext context, string tenantIdOrName)
    {
        var store = context.RequestServices.GetService<ITenantStore>()
            ?? throw new InvalidOperationException(
                "未注册 ITenantStore。持有租户注册表的宿主使用 AddMultiTenancyEfCore<TDbContext>()，" +
                "仅消费租户 claim 的资源服务使用 AddInMemoryTenantStore()。");

        if (Guid.TryParse(tenantIdOrName, out var tenantId))
        {
            return await store.FindAsync(tenantId, context.RequestAborted);
        }

        var normalizer = context.RequestServices.GetRequiredService<ITenantNormalizer>();
        return await store.FindByNameAsync(normalizer.NormalizeName(tenantIdOrName)!, context.RequestAborted);
    }
}
