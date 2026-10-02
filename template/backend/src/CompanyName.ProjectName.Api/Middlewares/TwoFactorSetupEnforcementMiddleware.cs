#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
using CompanyName.ProjectName.Api.Auth;
using CompanyName.ProjectName.Application.Auth.Constants;
using Leistd.ExceptionHandling;
using Microsoft.AspNetCore.Authorization;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 受限会话只能调用完成两步验证设置所需的接口。
/// </summary>
/// <remarks>
#if (OpenIddictServer)
/// <para>放在中间件而不是默认授权策略里：<c>/connect/authorize</c>
/// 不带 <c>[Authorize]</c>——它自己判断登录态并签发授权码，授权管道不经过它，而受限会话恰恰不能在那里
/// 替第三方应用换到令牌。这里按路径与端点元数据统一拦，漏标的后果是"受限用户用不了"，而不是"受限用户什么都能做"。</para>
#else
/// <para>按路径与端点元数据统一拦，而不是逐个端点声明：漏标的后果是"受限用户用不了"，而不是"受限用户什么都能做"。</para>
#endif
/// <para>拒绝以 403 + <c>Auth:TwoFactorSetupRequired</c> 返回，界面据此把人带去设置页。
/// 页面本身不拦：前端路由守卫负责把受限用户带去设置页，服务端守住的是数据。</para>
/// </remarks>
public sealed class TwoFactorSetupEnforcementMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(claim => claim.Type == TwoFactorClaimTypes.SetupRequired) &&
            IsGuardedPath(context.Request.Path) &&
            context.GetEndpoint() is { } endpoint &&
            endpoint.Metadata.GetMetadata<AllowDuringTwoFactorSetupAttribute>() is null &&
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            // 错误码在不含本地化的形态下也要带：界面按它把人带去设置页
            throw new BusinessException(AuthErrorCodes.TwoFactorSetupRequired, "Set up two-factor authentication before continuing.");
        }

        return next(context);
    }

    // 只管接口与授权端点。页面（SPA 回退、静态文件）照常下发：拦了它们，受限用户连设置页都打不开——
    // 整页刷新任何一个地址都会得到一段 403 JSON，而不是由前端路由守卫把人带去设置页
    private static bool IsGuardedPath(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/connect");
}
#endif
