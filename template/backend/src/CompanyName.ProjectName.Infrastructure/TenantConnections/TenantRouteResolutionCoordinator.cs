#if (!LocalIdentity)
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 同租户并发解析的合并器（单飞）。宿主级 singleton
/// </summary>
/// <remarks>
/// 协调器按宿主注册为 singleton，使同租户并发请求共享一次回源，同时避免不同
/// ServiceProvider 共享状态。共享任务在协调器创建的独立作用域中执行，生命周期不依赖
/// 任一发起请求；调用方取消等待不会释放任务使用的 scoped 服务。
/// </remarks>
internal sealed class TenantRouteResolutionCoordinator(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inflight =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 取得该键上正在进行的解析，没有则用 <paramref name="factory"/> 发起一次
    /// </summary>
    /// <remarks>
    /// <para>返回 <see cref="Lazy{T}"/> 而不是直接 await：调用方要用自己的取消令牌等待，
    /// 共享任务本身不能绑在任一调用方的生命周期上。</para>
    /// <para><paramref name="factory"/> 收到的 <see cref="IServiceProvider"/> 属于本协调器
    /// 新开的作用域，其生存期覆盖整个共享任务；<b>不要</b>在 factory 里改用捕获的外层依赖。</para>
    /// </remarks>
    public Lazy<Task<string>> GetOrStart(string key, Func<string, IServiceProvider, Task<string>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        return _inflight.GetOrAdd(
            key,
            k => new Lazy<Task<string>>(() => RunInOwnScopeAsync(k, factory), LazyThreadSafetyMode.ExecutionAndPublication));
    }

    /// <summary>解析结束后摘除条目</summary>
    public void Complete(string key) => _inflight.TryRemove(key, out _);

    private async Task<string> RunInOwnScopeAsync(string key, Func<string, IServiceProvider, Task<string>> factory)
    {
        // 作用域随共享任务一起结束，与任何调用方的请求作用域无关
        await using var scope = scopeFactory.CreateAsyncScope();
        return await factory(key, scope.ServiceProvider);
    }
}
#endif
