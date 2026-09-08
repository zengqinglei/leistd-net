#if (LocalIdentity)
using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Application.Auth;
#endif
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
using Leistd.Security.Claims;
#endif
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
#if (OpenIddictServer)
using OpenIddict.Abstractions;
using System.Security.Claims;
#endif
#if (OpenIddictServer || !LocalIdentity)
using OpenIddict.Validation.AspNetCore;
#endif

namespace CompanyName.ProjectName.Api;

/// <summary>
/// Api 层依赖注入配置
/// </summary>
/// <remarks>
/// 授权策略属于 Api 层职责（HTTP 认证方案与 Requirement），因此收在本文件而不是 <c>Program.cs</c>：
/// 与其余三层各自的 <c>DependencyInjection.cs</c> 同构，组合根只负责调用。
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Api 层授权：默认策略、相关 Handler 与内部控制面策略
    /// </summary>
    /// <remarks>
    /// 两种形态都在这里定案，组合根只有一行调用：本地身份形态的主体来自 Bearer 或会话 Cookie
    /// 并要求账号可用；资源服务形态只有 Bearer、账号状态由签发方负责。
    /// </remarks>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
#if (LocalIdentity)
        // 撤权要对已签发的凭据生效：登录时的启用/锁定检查挡不住已在线的会话。
        // 覆盖范围是每一次新的 HTTP 请求和每一次新的 Hub 连接握手；
        // 已经建立的 SignalR 连接不在其中，见 ActiveUserRequirement 的说明。
        services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();

        // 账号失效要返回 401 而不是 403：前端只把 401 当会话失效来清理登录态。
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, InvalidAccountResultHandler>();
#endif

        services.AddAuthorization(options =>
        {
#if (!LocalIdentity)
            // 资源服务只认签发方的 Bearer：这里没有用户表，账号是否可用由签发方在发令牌时判定
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
#else
            // 自然人主体可以来自两种方案：Bearer（签发形态）与会话 Cookie
            var humanSchemes = new[]
            {
#if (OpenIddictServer)
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
#endif
                AuthenticationSchemeNames.SessionCookie
            };

            // 撤权要对已签发的凭据生效：登录时的启用/锁定检查挡不住已在线的会话。
            // ActiveUserRequirement 覆盖每一次新的 HTTP 请求与每一次新的 Hub 握手
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(humanSchemes)
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveUserRequirement())
                .Build();

#if (OpenIddictServer)
            AddMachineScopePolicy(options, TenantConnectionPolicies.RuntimeRead, TenantConnectionScopes.RuntimeRead);
            AddMachineScopePolicy(options, TenantConnectionPolicies.MigrationRead, TenantConnectionScopes.MigrationRead);
#endif
#endif
        });

        return services;
    }

#if (OpenIddictServer)
    /// <summary>
    /// 注册一条只对<b>机器主体</b>开放的内部控制面策略
    /// </summary>
    /// <remarks>
    /// 策略只接受 Bearer 认证的 <c>client:&lt;client_id&gt;</c> 机器主体，并要求对应 scope；
    /// Runtime 与 Migration scope 彼此独立。超管 claim 和会话 Cookie 均不能旁路该边界，
    /// 机器访问通过停用 Open Application 或撤销 scope 收回。
    /// </remarks>
    private static void AddMachineScopePolicy(
        AuthorizationOptions options,
        string policyName,
        string requiredScope)
    {
        options.AddPolicy(policyName, policy => policy
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                ClientSubject.IsMachine(context.User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value) &&
                HasScope(context.User, requiredScope)));
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope) =>
        principal.Claims.Any(claim =>
            claim.Type == OpenIddictConstants.Claims.Scope &&
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(requiredScope, StringComparer.Ordinal));
#endif
}
