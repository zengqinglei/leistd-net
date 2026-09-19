using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 同租户并发解析的合并器（单飞），按宿主注册为单例。
// 共享任务在协调器新开的作用域中执行，生命周期不依赖任一发起请求：
// 调用方取消等待不会释放任务正在使用的 Scoped 服务。
internal sealed class TenantRouteResolutionCoordinator(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inflight = new(StringComparer.Ordinal);

    // 返回 Lazy 而不是直接 await：调用方用自己的取消令牌等待，共享任务不绑在任一调用方上。
    // factory 收到的 IServiceProvider 属于本协调器新开的作用域，不要在 factory 里改用捕获的外层依赖。
    public Lazy<Task<string>> GetOrStart(string key, Func<string, IServiceProvider, Task<string>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        return _inflight.GetOrAdd(
            key,
            k => new Lazy<Task<string>>(() => RunInOwnScopeAsync(k, factory), LazyThreadSafetyMode.ExecutionAndPublication));
    }

    public void Complete(string key) => _inflight.TryRemove(key, out _);

    private async Task<string> RunInOwnScopeAsync(string key, Func<string, IServiceProvider, Task<string>> factory)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await factory(key, scope.ServiceProvider);
    }
}
