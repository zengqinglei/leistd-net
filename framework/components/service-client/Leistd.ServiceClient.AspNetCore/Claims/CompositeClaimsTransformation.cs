using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Leistd.ServiceClient.AspNetCore.Claims;

/// <summary>
/// 按顺序执行两个 <see cref="IClaimsTransformation"/>：先宿主既有的（租户、外部身份等
/// claims 富化），再服务用户上下文恢复。
/// </summary>
/// <remarks>
/// ASP.NET Core 的认证服务只消费**单个** <see cref="IClaimsTransformation"/>，
/// 后注册者覆盖先注册者。<c>AddServiceUserContext</c> 因此不能简单替换——那会静默删掉
/// 宿主已注册的转换；改为把既有实现包进本组合，两者都生效。
///
/// 顺序固定为「宿主在前、恢复在后」：恢复会更换主身份（用户身份置于首位），
/// 应基于宿主富化后的主体进行。
/// </remarks>
/// <param name="inner">宿主既有的转换（含框架默认的空实现）</param>
/// <param name="serviceUserContext">服务用户上下文恢复转换</param>
/// <param name="ownsInner">
/// 本组合是否拥有 <paramref name="inner"/> 的释放责任。原注册是实现类型或工厂时，
/// 内层由本组合创建、DI 不再跟踪它，所有权随之转移；原注册是宿主自行 <c>new</c> 的实例
/// （<c>ImplementationInstance</c>）时容器本就不拥有它，释放责任留在宿主。
/// </param>
internal sealed class CompositeClaimsTransformation(
    IClaimsTransformation inner,
    ServiceUserContextClaimsTransformation serviceUserContext,
    bool ownsInner) : IClaimsTransformation, IDisposable, IAsyncDisposable
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var enriched = await inner.TransformAsync(principal);
        return await serviceUserContext.TransformAsync(enriched);
    }

    /// <summary>
    /// 同步释放内层（若本组合拥有它）。DI 跟踪本组合，因此作用域结束 / 容器关闭时会调到这里。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 内层**仅**实现 <see cref="IAsyncDisposable"/> 时抛出：与原生 DI 一致——它对这类服务
    /// 被同步释放同样抛错，要求改用异步作用域。所有权转移到本组合后，这一行为也随之转移，
    /// 静默跳过会把每请求（Scoped）或容器级（Singleton）的资源泄漏藏起来。
    /// 这里不用 <c>GetAwaiter().GetResult()</c> 兜底：sync-over-async 有死锁风险，
    /// 且同样掩盖了「该用异步作用域」这一事实。
    /// </exception>
    public void Dispose()
    {
        if (!ownsInner)
        {
            return;
        }

        switch (inner)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case IAsyncDisposable:
                throw new InvalidOperationException(
                    $"'{inner.GetType()}' type only implements IAsyncDisposable. " +
                    "Use DisposeAsync to dispose the container/scope.");
        }
    }

    /// <summary>
    /// 异步释放内层（若本组合拥有它）：优先 <see cref="IAsyncDisposable"/>，
    /// 否则回退同步释放。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!ownsInner)
        {
            return;
        }

        switch (inner)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
