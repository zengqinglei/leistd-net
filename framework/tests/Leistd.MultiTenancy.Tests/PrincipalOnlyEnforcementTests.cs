using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.MultiTenancy.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.MultiTenancy.Resolution;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// <c>ValidateResolvedTenant = false</c> 时解析链必须被强制收口为只有主体贡献者，
/// 无论宿主怎么改链。
/// </summary>
/// <remarks>
/// <para>这是一道安全边界。仅在"装配默认链"时少加贡献者是不够的——宿主有两种写法能绕过：
/// 在 <c>AddMultiTenancy</c> 之前注册贡献者（链非空，默认装配直接跳过），
/// 或在之后追加。此形态下注册表校验已关闭，未经验证的来源会被直接采信，
/// 匿名请求改一个请求头就能拿到任意租户上下文。因此收窄必须发生在
/// 所有 <c>Configure</c> 之后（<c>IPostConfigureOptions</c>）。</para>
/// <para>三类反例分别对应：默认装配、宿主前置、宿主后置。缺任何一类，
/// 对应的绕过路径就没有回归锁。</para>
/// </remarks>
public class PrincipalOnlyEnforcementTests
{
    [Fact]
    public void Default_assembly_keeps_only_the_principal_contributor()
    {
        var contributors = ResolveChain(services =>
            services.AddMultiTenancy(options => options.ValidateResolvedTenant = false));

        Assert.Single(contributors);
        Assert.IsType<CurrentPrincipalTenantResolveContributor>(contributors[0]);
    }

    /// <summary>
    /// 宿主在 <c>AddMultiTenancy</c> 之前注册贡献者：链非空使默认装配跳过，
    /// 只在装配默认链时收窄的话，这些未验证来源会原样留在链里。
    /// </summary>
    [Fact]
    public void Host_registered_contributors_before_setup_are_removed()
    {
        var contributors = ResolveChain(services =>
        {
            services.Configure<TenantResolveOptions>(options =>
            {
                options.Contributors.Add(new HeaderTenantResolveContributor());
                options.Contributors.Add(new QueryStringTenantResolveContributor());
            });
            services.AddMultiTenancy(options => options.ValidateResolvedTenant = false);
        });

        Assert.Single(contributors);
        Assert.IsType<CurrentPrincipalTenantResolveContributor>(contributors[0]);
    }

    /// <summary>
    /// 宿主在 <c>AddMultiTenancy</c> 之后追加贡献者：这是最自然的扩展写法，
    /// 也最容易在评审时被当成"宿主的自由"而放过。
    /// </summary>
    [Fact]
    public void Host_appended_contributors_after_setup_are_removed()
    {
        var contributors = ResolveChain(services =>
        {
            services.AddMultiTenancy(options => options.ValidateResolvedTenant = false);
            services.Configure<TenantResolveOptions>(options =>
            {
                options.Contributors.Add(new DomainTenantResolveContributor());
                options.Contributors.Add(new HeaderTenantResolveContributor());
            });
        });

        Assert.Single(contributors);
        Assert.IsType<CurrentPrincipalTenantResolveContributor>(contributors[0]);
    }

    /// <summary>
    /// 校验开启（默认）时不动链——那种形态下未经验证的来源仍会经注册表校验，
    /// 收口反而会砍掉子域名与请求头这两条合法路径。
    /// </summary>
    [Fact]
    public void Validation_enabled_keeps_the_full_chain()
    {
        // 开着注册表校验就必须有 ITenantStore，否则 TenantStoreRegistrationValidator 拦下——
        // 这里注册配置型 store，等价于一个合法的 Identity 形态宿主
        var contributors = ResolveChain(services =>
        {
            services.AddMultiTenancy();
            services.AddInMemoryTenantStore(_ => { });
        });

        Assert.Equal(4, contributors.Count);
        Assert.IsType<CurrentPrincipalTenantResolveContributor>(contributors[0]);
    }

    /// <summary>
    /// 开着注册表校验却没注册 <c>ITenantStore</c>：必须在读取选项时就失败，不能留到运行期
    /// </summary>
    /// <remarks>
    /// 此前这个前提只在中间件里发现——每个带租户的请求失败一次、而进程"健康"地跑着。
    /// </remarks>
    [Fact]
    public void Validation_enabled_without_a_tenant_store_fails_fast()
    {
        var error = Assert.Throws<OptionsValidationException>(
            () => ResolveChain(services => services.AddMultiTenancy()));

        Assert.Contains("no ITenantStore is registered", error.Message, StringComparison.Ordinal);
    }

    private static IList<ITenantResolveContributor> ResolveChain(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TenantResolveOptions>>().Value.Contributors;
    }
}
