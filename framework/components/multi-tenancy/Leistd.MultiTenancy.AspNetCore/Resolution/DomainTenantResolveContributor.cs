using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 从主机名解析租户，格式形如 <c>{0}.example.com</c>（<c>acme.example.com</c> → <c>acme</c>）
/// </summary>
/// <remarks>
/// <para>这是 SaaS 的主流形态，也是**便利性与安全性同时最优**的一种：租户写在 URL 里，
/// 登录页不需要租户字段，用户什么都不用记；同时不存在任何"列出全部租户"的接口，
/// 访客必须先知道域名才能到达那个页面。</para>
/// <para>位置在解析链中排在 Claim 贡献者**之后**、头与查询串**之前**：
/// 已认证主体的租户仍由 claim 定案（防跨租户水平越权的关键顺序不变）；
/// 而对匿名请求，子域名部署下域名就是权威，不允许再被请求头改写。</para>
/// <para><b>部署注意</b>：只把真正用于租户的通配子域指向本应用。若把整个顶级域通配过来，
/// <c>www.example.com</c> 会被解析成名为 <c>www</c> 的租户并因查不到而 404——
/// 这类保留子域应指向别处，或不要与租户共用同一段通配。</para>
/// </remarks>
public class DomainTenantResolveContributor : ITenantResolveContributor
{
    /// <summary>格式里代表租户名的占位符</summary>
    private const string TenantPlaceholder = "{0}";

    /// <inheritdoc />
    public string Name => "Domain";

    /// <inheritdoc />
    public Task ResolveAsync(TenantResolveContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.DomainFormat))
        {
            return Task.CompletedTask;
        }

        var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        // 只取主机名：端口属于部署形态，写进格式会让开发（:5240）与生产各配一份
        var host = httpContext.Request.Host.Host;
        var tenant = Extract(host, options.DomainFormat);

        if (!string.IsNullOrWhiteSpace(tenant))
        {
            context.TenantIdOrName = tenant;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 按格式从主机名中取出租户段；不匹配时返回 null（交由链上后续贡献者处理）
    /// </summary>
    internal static string? Extract(string host, string format)
    {
        var placeholderIndex = format.IndexOf(TenantPlaceholder, StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            return null;
        }

        var prefix = format[..placeholderIndex];
        var suffix = format[(placeholderIndex + TenantPlaceholder.Length)..];

        // 主机名大小写不敏感（RFC 4343）
        if (!host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tenantLength = host.Length - prefix.Length - suffix.Length;
        if (tenantLength <= 0)
        {
            // 恰好等于基础域（example.com）时长度为 0：那是宿主入口，不是某个租户
            return null;
        }

        var tenant = host.Substring(prefix.Length, tenantLength);

        // 租户段本身不能再含点号：a.b.example.com 不应被当作名为 "a.b" 的租户
        return tenant.Contains('.') ? null : tenant;
    }
}
