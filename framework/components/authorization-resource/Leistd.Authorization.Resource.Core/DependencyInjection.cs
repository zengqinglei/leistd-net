using Leistd.ExceptionHandling.Options;
using Leistd.Authorization.Resource.ExceptionMappings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Authorization.Resource.Services;
using Leistd.Authorization.Resource.Grants;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.Authorization.Resource.Abstractions;
using Leistd.Authorization.Resource.Errors;
using Leistd.Localization;

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
    /// <example>
    /// <code>
    /// builder.Services.AddResourceAuthorizationCore();
    /// builder.Services.AddResourceAuthorizationHandler&lt;Order, OrderOwnerHandler&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddResourceAuthorizationCore(this IServiceCollection services)
    {
        services.TryAddScoped<IResourceAuthorizationService, DefaultResourceAuthorizationService>();
        services.AddJsonLocalizationResources(typeof(ResourceAuthorizationErrorCodes).Assembly);
        // 错误码的状态语义与默认译文同属本组件的默认值，一并在这里登记：
        // 交给宿主逐个 Configure 的话，漏一个不会有编译或启动错误，只会静默回落成 400。
        // 宿主的 MapCode / MapException 覆盖同一码或同一类型，与调用顺序无关。
        services.Configure<GlobalExceptionOptions>(ResourceAuthorizationExceptionMappings.Configure);
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
