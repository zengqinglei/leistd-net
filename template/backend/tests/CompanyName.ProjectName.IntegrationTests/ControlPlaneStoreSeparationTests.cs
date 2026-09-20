#if (LocalIdentity)
using System.Text.RegularExpressions;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 控制面与业务数据落在**不同的物理存储**上，不只是模型里不互相映射。
/// </summary>
/// <remarks>
/// <para><c>ControlPlaneModelSeparationTests</c> 验的是"哪个实体属于哪个上下文"。这条验的是
/// 另一回事：两个上下文最终连到哪。两者都过不代表隔离成立——把
/// <c>UseInMemoryFallback(options, "-control")</c> 的后缀去掉，模型边界一字未改，
/// 但控制面与业务从此共用一个存储，而模型测试仍然全绿。</para>
/// <para>真实库形态下同一条约束由迁移历史表区分（见
/// <c>DatabaseSchema.ControlMigrationsHistoryTable</c>），共用存储会让两套迁移互相覆盖。
/// 这里在内存形态下把"必须是两个存储"这件事本身钉住，代价是一次服务解析。</para>
/// <para>租户专属连接的路由隔离不在这里：本夹具只有内存库，登记了连接的租户会走到
/// <c>UseNpgsql</c>，那条路要用真实 PostgreSQL 的集成测试覆盖。见
/// <see cref="ProjectWebApplicationFactory"/> 的说明。</para>
/// </remarks>
public sealed class ControlPlaneStoreSeparationTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public void Control_plane_and_business_contexts_use_distinct_stores()
    {
        using var scope = factory.Services.CreateScope();

        var control = StoreNameOf(scope.ServiceProvider.GetRequiredService<IdentityControlDbContext>());
        var business = StoreNameOf(scope.ServiceProvider.GetRequiredService<MyProjectDbContext>());

        // 取不到存储名说明判据本身失效了（EF 改了 LogFragment），必须当失败处理
        Assert.NotNull(control);
        Assert.NotNull(business);
        Assert.NotEqual(business, control);
    }

#if (OpenIddictServer)
    /// <summary>
    /// OIDC 存储与控制面同库不同迁移历史，内存形态下体现为第三个独立存储。
    /// </summary>
    [Fact]
    public void OpenIddict_context_uses_its_own_store()
    {
        using var scope = factory.Services.CreateScope();

        var control = StoreNameOf(scope.ServiceProvider.GetRequiredService<IdentityControlDbContext>());
        var business = StoreNameOf(scope.ServiceProvider.GetRequiredService<MyProjectDbContext>());
        var openIddict = StoreNameOf(scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>());

        Assert.NotNull(openIddict);
        Assert.NotEqual(control, openIddict);
        Assert.NotEqual(business, openIddict);
    }
#endif

    // 存储名只在内存提供程序的选项里，DbContext 上没有公开成员能直接拿到。
    // 走 IDbContextOptionsExtension.Info.LogFragment（公开契约）而不是 InMemoryOptionsExtension
    // （EF 内部 API，直接引用会报 EF1001，且 EF 小版本就可能改掉）。
    // 匹配不到时返回 null，由调用方断言失败——EF 若改了这段文本，这里要响，不能静默放行。
    private static string? StoreNameOf(DbContext context)
    {
        var fragments = string.Concat(
            context.GetService<IDbContextOptions>().Extensions.Select(e => e.Info.LogFragment));
        var match = Regex.Match(fragments, @"StoreName=(?<name>\S+)");
        return match.Success ? match.Groups["name"].Value : null;
    }
}
#endif
