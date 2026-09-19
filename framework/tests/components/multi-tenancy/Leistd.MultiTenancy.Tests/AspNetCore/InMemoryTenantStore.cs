using Leistd.MultiTenancy.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.MultiTenancy.Tests.AspNetCore;

/// <summary>
/// 测试用的配置型 <see cref="ITenantStore"/>：让"宿主形态"的用例有一个注册表可查。
/// </summary>
/// <remarks>
/// <para>本类曾是 <c>Leistd.MultiTenancy.Core</c> 的公开 API，2026-09-16 移来测试项目。
/// 移动理由：它承诺的场景在本框架里不存在——持有注册表的宿主用 EF 实现，
/// 资源服务把 <c>ValidateResolvedTenant</c> 置为 <see langword="false"/> 后根本不查 Store。
/// 而 <c>IsActive</c> 是访问控制状态，配置型清单改一次要重启进程，
/// 陈旧窗口比框架明令禁止的缓存还长（见 <c>EfCoreTenantStore</c> 的"刻意不缓存"）。</para>
/// <para>这里只服务两类用例：中间件要校验租户存在与启用，以及启动期校验要看到
/// 一个已注册的 <see cref="ITenantStore"/>。</para>
/// </remarks>
internal sealed class InMemoryTenantStore(InMemoryTenantStoreOptions options) : ITenantStore
{
    public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(options.Tenants.FirstOrDefault(t => t.Id == id));

    public Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
        => Task.FromResult(options.Tenants.FirstOrDefault(
            t => string.Equals(t.NormalizedName, normalizedName, StringComparison.Ordinal)));
}

/// <summary>配置 <see cref="InMemoryTenantStore"/> 的租户清单。</summary>
internal sealed class InMemoryTenantStoreOptions
{
    public IList<TenantConfiguration> Tenants { get; } = [];
}

internal static class InMemoryTenantStoreExtensions
{
    /// <summary>注册测试用的配置型租户注册表。</summary>
    public static IServiceCollection AddInMemoryTenantStore(
        this IServiceCollection services,
        Action<InMemoryTenantStoreOptions> configure)
    {
        var options = new InMemoryTenantStoreOptions();
        configure(options);

        // 注册成实现实例而非工厂：TenantStoreRegistrationValidator 用
        // IServiceProviderIsService 探测注册面，两种写法都能被探测到。
        services.AddSingleton<ITenantStore>(new InMemoryTenantStore(options));
        return services;
    }
}
