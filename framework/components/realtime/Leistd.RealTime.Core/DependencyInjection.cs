using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.RealTime.Abstractions;
using Leistd.RealTime.Services;

namespace Leistd.RealTime;

/// <summary>
/// 提供实时核心服务注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册实时核心能力。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddRealTime();
    ///
    /// await businessEventPublisher.PublishToResourceAsync($"Products:{productId}", "Updated", payload, ct);
    /// </code>
    /// </example>
    /// <remarks>
    /// <b>不注册默认订阅授权器。</b>框架不知道资源语义，给不出正确默认值，而"允许任何人
    /// 订阅任意资源"是一个必须由宿主明确做出的决定。公共资源场景显式注册
    /// <see cref="AllowAllRealTimeSubscriptionAuthorizer"/>；未注册时 <c>MapRealTimeHub()</c>
    /// 让宿主起不来，而不是静默放行。
    /// </remarks>
    public static IServiceCollection AddRealTime(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// 注册"允许订阅任意资源"的授权器——公共资源场景的显式选择。
    /// </summary>
    public static IServiceCollection AddAllowAllRealTimeSubscriptions(this IServiceCollection services)
    {
        services.TryAddSingleton<IRealTimeSubscriptionAuthorizer, AllowAllRealTimeSubscriptionAuthorizer>();
        return services;
    }
}
