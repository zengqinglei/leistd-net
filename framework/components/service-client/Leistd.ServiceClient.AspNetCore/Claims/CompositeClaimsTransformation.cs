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
internal sealed class CompositeClaimsTransformation(
    IClaimsTransformation inner,
    ServiceUserContextClaimsTransformation serviceUserContext) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var enriched = await inner.TransformAsync(principal);
        return await serviceUserContext.TransformAsync(enriched);
    }
}
