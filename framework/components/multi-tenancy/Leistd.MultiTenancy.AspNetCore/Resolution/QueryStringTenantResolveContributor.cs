using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 从查询串解析租户（默认参数名 <c>tenant</c>），服务于邮件验证、找回密码等匿名链路的链接场景
/// </summary>
public class QueryStringTenantResolveContributor : ITenantResolveContributor
{
    /// <inheritdoc />
    public string Name => "QueryString";

    /// <inheritdoc />
    public Task ResolveAsync(TenantResolveContext context)
    {
        var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        var options = context.ServiceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;
        var value = httpContext.Request.Query[options.QueryStringParameterName].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(value))
        {
            context.TenantIdOrName = value;
        }

        return Task.CompletedTask;
    }
}
