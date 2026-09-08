using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Exceptions;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.MultiTenancy.Resolution;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.AspNetCore.Middlewares;

/// <summary>
/// 解析并校验租户，然后在租户上下文中执行后续管道。
/// </summary>
/// <remarks>
/// 放置顺序：<c>UseAuthentication()</c>（及 <c>UseServiceUserContext()</c>）之后、<c>UseAuthorization()</c> 之前。
/// 解析出的租户不存在抛 <see cref="TenantNotFoundException"/>（404），已停用抛 <see cref="TenantNotActiveException"/>（403）；
/// <b>未解析出租户不是错误，即宿主上下文</b>。
/// 资源服务与宿主共用本中间件，差别只在 <c>MultiTenancyOptions.ValidateResolvedTenant</c>。
/// </remarks>
public class MultiTenancyMiddleware(RequestDelegate next, ILogger<MultiTenancyMiddleware> logger)
{
    /// <summary>
    /// 处理当前请求。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var resolver = context.RequestServices.GetRequiredService<ITenantResolver>();
        var result = await resolver.ResolveAsync();

        // 记录实际解析链，便于定位由哪个来源确定了租户。
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Tenant resolve chain applied [{AppliedResolvers}] and produced {TenantIdOrName}",
                string.Join(" -> ", result.AppliedResolvers),
                result.TenantIdOrName ?? "<host>");
        }

        if (result.TenantIdOrName is null)
        {
            await next(context);
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

        Guid tenantId;
        string? tenantName = null;

        if (options.ValidateResolvedTenant)
        {
            var tenant = await FindTenantAsync(context, result.TenantIdOrName);
            if (tenant is null)
            {
                throw new TenantNotFoundException(result.TenantIdOrName);
            }

            if (!tenant.IsActive)
            {
                throw new TenantNotActiveException(result.TenantIdOrName);
            }

            tenantId = tenant.Id;
            tenantName = tenant.Name;
        }
        else
        {
            // 不校验形态下解析链只有主体贡献者，值来自已验证令牌的 claim；
            // 签发方在发这个 claim 之前已校验过租户，此处只需确认它是个合法 Id。
            // 名称不可得（没有注册表可查），保持 null——它只用于日志与展示
            if (!Guid.TryParse(result.TenantIdOrName, out tenantId) || tenantId == Guid.Empty)
            {
                throw new TenantNotFoundException(result.TenantIdOrName);
            }
        }

        var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();

        // 租户上下文必须覆盖整个下游管道。
        using (currentTenant.Change(tenantId, tenantName))
        using (logger.BeginScope(new Dictionary<string, object> { ["leistd.tenantId"] = tenantId }))
        {
            await next(context);
        }
    }

    private static async Task<TenantConfiguration?> FindTenantAsync(HttpContext context, string tenantIdOrName)
    {
        // 未注册 ITenantStore 已由 TenantStoreRegistrationValidator 在启动期拦下，
        // 这里的兜底只为覆盖"绕过 AddMultiTenancy 自行拼装管道"这种非常规接法
        var store = context.RequestServices.GetRequiredService<ITenantStore>();

        if (Guid.TryParse(tenantIdOrName, out var tenantId))
        {
            return await store.FindAsync(tenantId, context.RequestAborted);
        }

        var normalizer = context.RequestServices.GetRequiredService<ITenantNormalizer>();
        return await store.FindByNameAsync(normalizer.NormalizeName(tenantIdOrName)!, context.RequestAborted);
    }
}
