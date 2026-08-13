using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 从已认证主体的租户 claim 解析租户。
/// <b>必须位于链首</b>：已认证用户的租户由 claim 定案且终止链（含"无 claim = 宿主用户"），
/// 请求头与查询串无法改写已登录用户的租户——这是防跨租户水平越权的关键顺序
/// </summary>
public class CurrentPrincipalTenantResolveContributor : ITenantResolveContributor
{
    /// <inheritdoc />
    public string Name => "CurrentPrincipal";

    /// <inheritdoc />
    public Task ResolveAsync(TenantResolveContext context)
    {
        var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var user = httpContext?.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

            // 有定论：有 claim 即租户，无 claim 即宿主，两种情况都终止链
            context.Handled = true;
            context.TenantIdOrName = user.FindFirst(options.TenantClaimType)?.Value;
        }

        return Task.CompletedTask;
    }
}
