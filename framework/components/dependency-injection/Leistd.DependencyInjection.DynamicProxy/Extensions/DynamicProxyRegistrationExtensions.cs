using Castle.DynamicProxy;
using Leistd.DependencyInjection.Abstractions;

namespace Leistd.DependencyInjection.DynamicProxy.Extensions;

/// <summary>
/// 提供服务注册回调的动态代理配置扩展。
/// </summary>
public static class DynamicProxyRegistrationExtensions
{
    private const string InterceptorsKey = "Leistd.DependencyInjection.DynamicProxy.Interceptors";

    /// <summary>
    /// 为当前服务注册追加拦截器类型；同一类型只追加一次。
    /// </summary>
    /// <remarks>组件的 <c>AddXxx</c> 被重复调用时会重复登记回调，去重避免同一拦截器被织入两层。</remarks>
    public static IOnServiceRegisteredContext AddInterceptor(
        this IOnServiceRegisteredContext context,
        Type interceptorType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interceptorType);

        var interceptors = context.GetInterceptorTypes();
        if (!interceptors.Contains(interceptorType))
        {
            interceptors.Add(interceptorType);
        }

        return context;
    }

    /// <summary>
    /// 获取当前服务注册上收集到的拦截器类型。
    /// </summary>
    public static List<Type> GetInterceptorTypes(this IOnServiceRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Items.TryGetValue(InterceptorsKey, out var value) || value is not List<Type> interceptors)
        {
            interceptors = [];
            context.Items[InterceptorsKey] = interceptors;
        }

        return interceptors;
    }
}
