using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Authorization;

/// <summary>
/// 权限定义与检查的核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册权限定义管理器。
    /// </summary>
    public static IServiceCollection AddAuthorizationCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
        return services;
    }
}
