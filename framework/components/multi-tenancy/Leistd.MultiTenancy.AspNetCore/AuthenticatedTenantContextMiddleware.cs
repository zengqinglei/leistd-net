using Leistd.Exception.Core;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy;

/// <summary>
/// Resource 宿主的可信租户中间件，只从已验证主体的唯一 <c>tenant_id</c> claim 建立上下文。
/// </summary>
public class AuthenticatedTenantContextMiddleware(
    RequestDelegate next,
    ILogger<AuthenticatedTenantContextMiddleware> logger)
{
    /// <summary>处理请求。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedException("An authenticated tenant principal is required.");
        }

        var claims = context.User.FindAll(CustomClaimTypes.TenantId).ToList();
        if (claims.Count != 1 || !Guid.TryParse(claims[0].Value, out var tenantId) || tenantId == Guid.Empty)
        {
            throw new UnauthorizedException("The authenticated principal must contain exactly one valid tenant_id claim.");
        }

        var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(tenantId))
        using (logger.BeginScope(new Dictionary<string, object> { ["leistd.tenantId"] = tenantId }))
        {
            await next(context);
        }
    }
}
