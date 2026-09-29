using Leistd.ExceptionHandling.Options;
using Leistd.Authorization.Resource.ExceptionMappings;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Authorization.Resource.Errors;
using Leistd.Localization;

namespace Leistd.Authorization.Resource;

/// <summary>
/// 资源实例授权核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 登记资源实例授权组件的错误码默认译文与默认 HTTP 状态。
    /// </summary>
    /// <remarks>
    /// 判定入口与 ACL 处理器在 <c>Leistd.Authorization.Resource.AspNetCore</c> 的 <c>AddResourceAuthorization()</c>，
    /// 资源 ACL 存储在 EF 包；两者都会调用本方法。
    /// </remarks>
    public static IServiceCollection AddResourceAuthorizationCore(this IServiceCollection services)
    {
        services.AddJsonLocalizationResources(typeof(ResourceAuthorizationErrorCodes).Assembly);
        // 错误码的状态语义与默认译文同属本组件的默认值，一并在这里登记：
        // 交给宿主逐个 Configure 的话，漏一个不会有编译或启动错误，只会静默回落成 400。
        // 宿主的 MapCode / MapException 覆盖同一码或同一类型，与调用顺序无关。
        services.Configure<GlobalExceptionOptions>(ResourceAuthorizationExceptionMappings.Configure);
        return services;
    }
}
