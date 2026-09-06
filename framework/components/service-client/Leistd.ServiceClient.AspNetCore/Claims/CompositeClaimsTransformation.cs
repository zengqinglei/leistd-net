using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Leistd.ServiceClient.AspNetCore.Claims;

// ASP.NET Core 只消费单个 IClaimsTransformation，因此组合宿主转换与服务用户恢复。
// 宿主转换先执行，恢复逻辑基于富化后的主体更换主身份。
// ownsInner 仅在组合自行创建内层实例时为真；宿主提供的实例仍由宿主管理生命周期。
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

    // 同步释放保持原生 DI 语义：仅支持异步释放的内层要求宿主异步释放作用域。
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

    // 异步释放优先使用 IAsyncDisposable，未实现时回退到 IDisposable。
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
