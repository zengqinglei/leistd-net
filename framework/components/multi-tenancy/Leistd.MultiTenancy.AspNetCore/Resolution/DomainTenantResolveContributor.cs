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

        // 只取主机名：端口属于部署形态，写进格式会让开发（:5240）与生产各配一份。
        //
        // 末尾根点要先去掉：DNS 里 acme.example.com. 与 acme.example.com 是同一个名字，
        // 而 HostString.Host 原样保留那个点。不规范化的话字面比较匹配不上，解析会退回请求头——
        // 匿名请求只要在 Host 末尾多打一个点，就绕过了"子域名是权威来源"这条边界。
        // 配置侧则要求不带根点（校验器拒绝），保证两边只有一种 canonical 形态。
        var host = httpContext.Request.Host.Host.TrimEnd('.');

        switch (Match(host, options.DomainFormat, out var tenant))
        {
            case HostMatch.Tenant:
                context.TenantIdOrName = tenant;
                break;

            case HostMatch.ManagedWithoutTenant:
                // 受管域内没有合法租户段（基础域、多级子域、前缀不匹配）：**就此定案为宿主**。
                // 不设 Handled 的话链会继续走到 Header，匿名请求在 example.com 上带个
                // X-Tenant-Id 就能挑任意租户——"域名是权威来源"当场失效
                context.Handled = true;
                break;

            case HostMatch.Outside:
                // 不属于受管域：交回链上后续贡献者。服务间调用打的是集群内部主机名、
                // 靠 X-Tenant-Id 传租户，一刀切会把它打断
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>主机名与受管域的关系</summary>
    private enum HostMatch
    {
        /// <summary>不属于受管域</summary>
        Outside,

        /// <summary>属于受管域，但没有合法的租户段</summary>
        ManagedWithoutTenant,

        /// <summary>解析出了租户段</summary>
        Tenant
    }

    /// <summary>
    /// 判定主机名与受管域的关系，并在可能时取出租户段
    /// </summary>
    /// <remarks>
    /// 三分而不是"匹配/不匹配"两分：受管域内没解析出租户时必须终止解析链（宿主），
    /// 与"根本不是这个域的请求"是两件事——后者要把决定权交回请求头。
    /// </remarks>
    private static HostMatch Match(string host, string format, out string? tenant)
    {
        tenant = null;

        var placeholderIndex = format.IndexOf(TenantPlaceholder, StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            return HostMatch.Outside;
        }

        var prefix = format[..placeholderIndex];
        var suffix = format[(placeholderIndex + TenantPlaceholder.Length)..];

        // 受管域的判定只看后缀：主机名以它结尾，或恰好等于去掉前导点的它（基础域本身）。
        // 主机名大小写不敏感（RFC 4343）
        var baseDomain = suffix.TrimStart('.');
        var endsWithSuffix = host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        var isBaseDomain = host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase);

        if (!endsWithSuffix && !isBaseDomain)
        {
            return HostMatch.Outside;
        }

        if (isBaseDomain || !host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // 基础域是宿主入口；前缀形态不匹配（other.example.com 之于 tenant-{0}.example.com）
            // 同样落在受管域内但不是租户
            return HostMatch.ManagedWithoutTenant;
        }

        var tenantLength = host.Length - prefix.Length - suffix.Length;
        if (tenantLength <= 0)
        {
            return HostMatch.ManagedWithoutTenant;
        }

        var candidate = host.Substring(prefix.Length, tenantLength);

        // 租户段本身不能再含点号：a.b.example.com 不应被当作名为 "a.b" 的租户
        if (candidate.Contains('.'))
        {
            return HostMatch.ManagedWithoutTenant;
        }

        tenant = candidate;
        return HostMatch.Tenant;
    }
}
