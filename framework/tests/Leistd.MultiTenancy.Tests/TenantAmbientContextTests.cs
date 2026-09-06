using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Leistd.AmbientContext;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.AspNetCore;
using Leistd.Security;
using Leistd.Security.Claims;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

// 环境上下文的租户维度：只认 claim，不跑完整解析链。
// 贡献者随 Leistd.MultiTenancy.AspNetCore 分发（claim 类型在它的 MultiTenancyOptions 上），
// 因此这里用 AddMultiTenancy 组装——它是个类库，Hub 调用与后台入口同样引用它。
public class TenantAmbientContextTests
{
    [Fact]
    public void Tenant_claim_is_established_for_the_scope_and_restored_after()
    {
        var tenantId = Guid.NewGuid();
        var (ambient, currentTenant) = Build();

        using (ambient.Begin(Authenticated((CustomClaimTypes.TenantId, tenantId.ToString()))))
        {
            Assert.Equal(tenantId, currentTenant.Id);
        }

        Assert.Null(currentTenant.Id);
    }

    // 无声明即宿主，但必须显式置位：入口可能继承了外层租户上下文。
    [Fact]
    public void Absent_tenant_claim_switches_to_the_host_view()
    {
        var outer = Guid.NewGuid();
        var (ambient, currentTenant) = Build();

        using (currentTenant.Change(outer))
        using (ambient.Begin(Authenticated()))
        {
            Assert.Null(currentTenant.Id);
        }
    }

    // 有声明但不是 Guid：签发端与消费端口径不一致，属配置错误。
    // 绝不能退回宿主视角——那会把租户用户静默放进宿主分区。
    [Fact]
    public void Malformed_tenant_claim_fails_closed()
    {
        var (ambient, currentTenant) = Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => ambient.Begin(Authenticated((CustomClaimTypes.TenantId, "not-a-guid"))));

        Assert.Contains("not a GUID", error.Message);
        Assert.Null(currentTenant.Id);
    }

    // 未认证主体没有可信租户来源，不猜，保持进入前的状态。
    [Fact]
    public void Unauthenticated_principal_leaves_the_tenant_untouched()
    {
        var outer = Guid.NewGuid();
        var (ambient, currentTenant) = Build();

        using (currentTenant.Change(outer))
        using (ambient.Begin(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            Assert.Equal(outer, currentTenant.Id);
        }
    }

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test"));

    private static (IAmbientContext Ambient, ICurrentTenant CurrentTenant) Build()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddAmbientContext()
            // 声明即定案，不查注册表——正是本贡献者服务的形态。
            .AddMultiTenancy(o => o.ValidateResolvedTenant = false)
            .BuildServiceProvider();

        return (sp.GetRequiredService<IAmbientContext>(), sp.GetRequiredService<ICurrentTenant>());
    }
}
