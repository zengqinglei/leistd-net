using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// <see cref="ITenantResolver"/> 默认实现：按 <see cref="TenantResolveOptions.Contributors"/> 顺序执行，
/// 首个有定论（解析出租户或确定为宿主）的贡献者终止链
/// </summary>
/// <remarks>
/// 以 Scoped 注册：注入的 <paramref name="serviceProvider"/> 即当前请求作用域，
/// 贡献者可从中解析 <c>IHttpContextAccessor</c> 等服务。
/// </remarks>
public class TenantResolver(IServiceProvider serviceProvider, IOptions<TenantResolveOptions> options) : ITenantResolver
{
    /// <inheritdoc />
    public async Task<TenantResolveResult> ResolveAsync()
    {
        var result = new TenantResolveResult();
        var context = new TenantResolveContext(serviceProvider);

        foreach (var contributor in options.Value.Contributors)
        {
            await contributor.ResolveAsync(context);
            result.AppliedResolvers.Add(contributor.Name);

            if (context.HasResolvedTenantOrHost())
            {
                result.TenantIdOrName = context.TenantIdOrName;
                break;
            }
        }

        return result;
    }
}
