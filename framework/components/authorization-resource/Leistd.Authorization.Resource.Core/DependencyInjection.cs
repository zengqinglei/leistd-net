using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Authorization.Resource;

/// <summary>
/// 资源实例授权核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册资源实例授权服务。
    /// </summary>
    /// <remarks>
    /// 依赖调用方已注册 <see cref="IPermissionSubjectProvider"/>。
    /// 若未注册 <see cref="IResourceGrantStore"/>，则只有领域规则处理器参与判定，
    /// 适合"只用所有者/成员规则、不需要显式 ACL"的项目。
    /// </remarks>
    public static IServiceCollection AddResourceAuthorizationCore(this IServiceCollection services)
    {
        services.TryAddScoped<IResourceAuthorizationService, DefaultResourceAuthorizationService>();
        return services;
    }

    /// <summary>
    /// 注册某个资源类型的领域规则处理器。同一资源类型可以注册多个。
    /// </summary>
    public static IServiceCollection AddResourceAuthorizationHandler<TResource, THandler>(
        this IServiceCollection services)
        where THandler : class, IResourceAuthorizationHandler<TResource>
    {
        services.AddScoped<IResourceAuthorizationHandler<TResource>, THandler>();
        return services;
    }
}
