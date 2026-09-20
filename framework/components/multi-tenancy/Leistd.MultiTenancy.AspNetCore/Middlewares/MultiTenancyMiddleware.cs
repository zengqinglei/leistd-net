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
/// 解析出的租户不存在抛 <see cref="TenantNotFoundException"/>（404）。已停用<b>分两档</b>：
/// 已认证主体抛 <see cref="TenantNotActiveException"/>（403，明确报错对运维有价值），
/// <b>未认证请求一律按 404</b>——"这个租户停用了"本身就是外部可观察的业务情报。
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

        // 记录是哪个来源确定了租户：排查"租户怎么来的"只需要这一个名字。
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Tenant resolved by {AppliedResolver} and produced {TenantIdOrName}",
                result.AppliedResolver ?? "<none>",
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

            // 未认证请求不区分"不存在"与"已停用"：两者给出不同响应，等于把**启用状态**告诉任何人，
            // 而"某个租户被停用了"是外部观察得到的业务情报。
            //
            // 不放行继续走：那样请求会落到宿主上下文，租户用户输错租户名时凭据会拿去和宿主用户比对。
            //
            // **这里没有隐藏存在性，也不打算隐藏。** 租户存在时请求走到业务逻辑（登录 401、
            // security-config 200），不存在时在这里被挡成 404，一次请求就能判定，不需要爆破。
            // 要抹平这个差别，得给不存在的租户编造注册策略与验证码，登录页会照着虚构的策略渲染——
            // 比泄露存在性更糟。这是有意接受的残留，理由与边界写在组件文档里，别当缺陷"修"掉。
            if (tenant is null || !tenant.IsActive)
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    throw new TenantNotFoundException(result.TenantIdOrName);
                }

                // 已认证主体只能探到自己的租户，明确报错对运维有价值
                throw tenant is null
                    ? new TenantNotFoundException(result.TenantIdOrName)
                    : new TenantNotActiveException(result.TenantIdOrName);
            }

            tenantId = tenant.Id;
            tenantName = tenant.Name;
        }
        else
        {
            // 该模式只采信已验证主体的租户 Id；没有注册表可查，名称保持 null。
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
