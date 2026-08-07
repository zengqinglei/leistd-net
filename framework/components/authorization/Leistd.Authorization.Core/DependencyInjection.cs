using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Authorization;

/// <summary>
/// 权限定义与检查的核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册权限定义管理器与默认权限检查器。
    /// </summary>
    /// <remarks>
    /// <see cref="IPermissionDefinitionManager"/> 为 Singleton（定义在启动时一次性加载并预计算）；
    /// <see cref="IPermissionChecker"/> 为 Scoped，使一次请求内的多次权限检查共享同一份主体与授予快照。
    /// </remarks>
    public static IServiceCollection AddPermissionAuthorizationCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
        services.TryAddScoped<IPermissionChecker, DefaultPermissionChecker>();
        return services;
    }
}
