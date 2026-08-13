using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 从请求头解析租户（默认 <c>X-Tenant-Id</c>）。
/// 仅对匿名请求生效（认证请求已被链首的 Claim 贡献者定案）——
/// 匿名头解析只决定"后续认证发生在哪个租户分区"，本身不授予任何数据可见性
/// </summary>
public class HeaderTenantResolveContributor : ITenantResolveContributor
{
    /// <inheritdoc />
    public string Name => "Header";

    /// <inheritdoc />
    public Task ResolveAsync(TenantResolveContext context)
    {
        var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        var options = context.ServiceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;
        var value = httpContext.Request.Headers[options.HeaderName].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(value))
        {
            context.TenantIdOrName = value;
        }

        return Task.CompletedTask;
    }
}
