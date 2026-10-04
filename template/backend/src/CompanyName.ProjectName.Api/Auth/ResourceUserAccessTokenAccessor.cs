#if (RemoteTokenAuth)
#if (ResourceBrowserSession)
using CompanyName.ProjectName.Application.Shared;
#endif
using Leistd.ServiceClient.Abstractions;
using Microsoft.AspNetCore.Authentication;
#if (!ResourceBrowserSession)
using OpenIddict.Validation.AspNetCore;
#endif

namespace CompanyName.ProjectName.Api.Auth;

#if (ResourceBrowserSession)
/// <summary>在请求期从已验证 Bearer 或服务端会话票据读取用户访问令牌。</summary>
#else
/// <summary>在请求期从已验证的 Bearer 读取用户访问令牌。</summary>
#endif
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
        return result.Succeeded ? result.Properties?.GetTokenValue("access_token") : null;
    }
}
#endif
