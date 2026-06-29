using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.RealTime;

/// <summary>
/// 实时核心组件依赖注入配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册实时核心能力。
    /// </summary>
    public static IServiceCollection AddRealTime(this IServiceCollection services)
    {
        services.TryAddSingleton<IRealtimeSubscriptionAuthorizer, AllowAllRealtimeSubscriptionAuthorizer>();
        return services;
    }
}
