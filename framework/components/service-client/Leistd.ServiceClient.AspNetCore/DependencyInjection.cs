using System.Net.Http.Headers;
using Leistd.ServiceClient.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.ServiceClient.AspNetCore;

/// <summary>ASP.NET Core 用户访问令牌适配。</summary>
public static class DependencyInjection
{
    /// <summary>注册请求时读取的访问令牌适配器。必须传入宿主的 Bearer 验证方案，不能传 Cookie 方案。</summary>
    public static IServiceCollection AddUserAccessTokenAccessor(this IServiceCollection services, string authenticationSchemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationSchemeName);
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IUserAccessTokenAccessor>(provider =>
            new HttpUserAccessTokenAccessor(provider.GetRequiredService<IHttpContextAccessor>(), authenticationSchemeName));
        return services;
    }

    private sealed class HttpUserAccessTokenAccessor(IHttpContextAccessor contexts, string scheme) : IUserAccessTokenAccessor
    {
        public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = contexts.HttpContext;
            if (context is null || !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) ||
                !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
                return null;
            // 在请求作用域认证，避免池化 handler 捕获请求；Cookie 认证成功不能替代 Bearer 验证。
            var result = await context.AuthenticateAsync(scheme);
            // 只读取认证票据保存的令牌，避免认证后改写请求头时交换未验证的值。
            return result.Succeeded && result.Principal?.Identity?.IsAuthenticated == true
                ? result.Properties?.GetTokenValue("access_token") : null;
        }
    }
}
