#if (RemoteTokenAuth)
#if (ResourceBrowserSession)
using CompanyName.ProjectName.Application.Shared;
#endif
using Leistd.ServiceClient.Abstractions;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace CompanyName.ProjectName.Api.Auth;

#if (ResourceBrowserSession)
/// <summary>在请求期从已验证 Bearer 或服务端会话票据读取用户访问令牌。</summary>
#else
/// <summary>在请求期从已验证的 Bearer 读取用户访问令牌。</summary>
#endif
/// <remarks>
/// 认证成功后读取官方保存的访问令牌。OIDC 会话与 OpenIddict Bearer 均使用
/// <see cref="OpenIddictValidationAspNetCoreConstants.Tokens.AccessToken"/>。
/// 失败结果也可能保存原令牌，因此必须先检查认证成功，才能将令牌交给下游。
/// </remarks>
internal sealed class ResourceUserAccessTokenAccessor(IHttpContextAccessor contexts) : IUserAccessTokenAccessor
{
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contexts.HttpContext is not { } context) return null;
#if (ResourceBrowserSession)
        var result = await context.AuthenticateAsync(AuthenticationSchemeNames.Smart);
#else
        var result = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
#endif
        return result.Succeeded ? result.Properties?.GetTokenValue(OpenIddictValidationAspNetCoreConstants.Tokens.AccessToken) : null;
    }
}
#endif
