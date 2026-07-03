using Microsoft.AspNetCore.Authorization;
using Leistd.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Authorization.AspNetCore;

/// <summary>
/// 权限授权（微软 Policy 管道）依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基于权限定义的动态授权策略：使 <c>[Authorize(Policy = "权限名")]</c> 生效。
    /// </summary>
    /// <remarks>
    /// 依赖调用方已注册 <see cref="IPermissionChecker"/> 与
    /// <see cref="IPermissionDefinitionManager"/>，并已调用 <c>AddAuthorization()</c>。
    /// </remarks>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddPermissionAuthorizationCore();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}

