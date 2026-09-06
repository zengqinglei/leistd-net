using Microsoft.Extensions.Logging;
using Leistd.MultiTenancy.Resolution;
using Microsoft.Extensions.Options;
using Leistd.MultiTenancy.AspNetCore.Options;

namespace Leistd.MultiTenancy.AspNetCore.Resolution;

// 不校验租户注册表时，把解析链强制收口为"只信已验证主体的 claim"
// IPostConfigureOptions{TOptions} 在所有宿主配置之后执行，
// 避免 Header、QueryString 或 Domain 贡献者在关闭注册表校验时被直接采信。
// 收窄是安全的形态，不是错误配置：即使宿主同时配了
// DomainFormat（例如全局共享一份配置、各服务只覆盖校验开关），
// 结果也只是子域名不参与解析、租户一律来自已验证的 claim。
// 因此这里不阻止启动，只在确实有配置被丢弃时记一条警告——
// 让"我配了子域名却不生效"这件事有迹可循。
internal sealed class PrincipalOnlyTenantResolveEnforcer(
    IOptions<MultiTenancyOptions> multiTenancyOptions,
    ILogger<PrincipalOnlyTenantResolveEnforcer> logger)
    : IPostConfigureOptions<TenantResolveOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, TenantResolveOptions options)
    {
        var multiTenancy = multiTenancyOptions.Value;
        if (multiTenancy.ValidateResolvedTenant)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(multiTenancy.DomainFormat))
        {
            logger.LogWarning(
                "MultiTenancyOptions.DomainFormat is set ('{DomainFormat}') but ValidateResolvedTenant is " +
                "false, so the resolve chain is narrowed to the principal contributor and subdomain " +
                "resolution will not run. Tenants come from the verified tenant claim only.",
                multiTenancy.DomainFormat);
        }

        options.Contributors.Clear();
        options.Contributors.Add(new CurrentPrincipalTenantResolveContributor());
    }
}
