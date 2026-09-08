using Castle.DynamicProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.DependencyInjection.Registration;
using Leistd.DependencyInjection.DynamicProxy.Extensions;
using Leistd.DynamicProxy.Interceptors;
using Leistd.DependencyInjection.Abstractions;

namespace Leistd.DependencyInjection.DynamicProxy.Registration;

/// <summary>
/// 支持 DynamicProxy 拦截器织入的服务注册回调工厂。
/// </summary>
public class DynamicProxyServiceRegistrationCallbackFactory : ServiceRegistrationCallbackFactory
{
    /// <summary>创建使用 Microsoft DI 默认校验选项的工厂。</summary>
    public DynamicProxyServiceRegistrationCallbackFactory()
    {
    }

    /// <summary>创建使用指定服务提供器校验选项的工厂。</summary>
    public DynamicProxyServiceRegistrationCallbackFactory(ServiceProviderOptions options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnRegistrationProcessed(
        IServiceCollection services,
        ServiceDescriptor descriptor,
        IOnServiceRegisteredContext context)
    {
        var interceptorTypes = context.GetInterceptorTypes();
        if (interceptorTypes.Count == 0)
            return;

        services.TryAddSingleton<IProxyGenerator, ProxyGenerator>();
        ApplyInterceptors(services, descriptor, interceptorTypes);
    }

    // 键控注册必须原样改写回键控描述符：改成非键控会让 GetKeyedService 直接取不到，
    // 而容器照常构建成功——正是本组件要消除的静默失效。
    private static void ApplyInterceptors(
        IServiceCollection services,
        ServiceDescriptor descriptor,
        List<Type> interceptorTypes)
    {
        var index = services.IndexOf(descriptor);
        if (index < 0)
            return;

        services[index] = descriptor.IsKeyedService
            ? ServiceDescriptor.DescribeKeyed(
                descriptor.ServiceType,
                descriptor.ServiceKey,
                (sp, key) => CreateProxy(descriptor, sp, key, interceptorTypes),
                descriptor.Lifetime)
            : ServiceDescriptor.Describe(
                descriptor.ServiceType,
                sp => CreateProxy(descriptor, sp, serviceKey: null, interceptorTypes),
                descriptor.Lifetime);
    }

    private static object CreateProxy(
        ServiceDescriptor descriptor,
        IServiceProvider sp,
        object? serviceKey,
        List<Type> interceptorTypes)
    {
        var instance = CreateOriginalInstance(descriptor, sp, serviceKey);
        var proxyGenerator = sp.GetRequiredService<IProxyGenerator>();
        var interceptors = interceptorTypes
            .Select(sp.GetRequiredService)
            .OrderBy(interceptor => interceptor is BaseAsyncInterceptor asyncInterceptor
                ? asyncInterceptor.Order
                : 0)
            .Select(ToCastleInterceptor)
            .ToArray();

        if (descriptor.ServiceType.IsInterface)
        {
            return proxyGenerator.CreateInterfaceProxyWithTarget(
                descriptor.ServiceType,
                instance,
                interceptors);
        }

        return proxyGenerator.CreateClassProxyWithTarget(
            descriptor.ServiceType,
            instance,
            interceptors);
    }

    private static IInterceptor ToCastleInterceptor(object interceptor) => interceptor switch
    {
        IInterceptor synchronous => synchronous,
        IAsyncInterceptor asynchronous => asynchronous.ToInterceptor(),
        _ => throw new InvalidOperationException(
            $"Interceptor '{interceptor.GetType().FullName}' must implement Castle IInterceptor or IAsyncInterceptor.")
    };

    // 键控与非键控的实现信息分挂在两组属性上，读错那一组只会得到 null，
    // 最终以"Cannot create instance"的形式在解析时才暴露。
    private static object CreateOriginalInstance(
        ServiceDescriptor descriptor,
        IServiceProvider sp,
        object? serviceKey)
    {
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationInstance != null)
                return descriptor.KeyedImplementationInstance;

            if (descriptor.KeyedImplementationFactory != null)
                return descriptor.KeyedImplementationFactory(sp, serviceKey);

            if (descriptor.KeyedImplementationType != null)
                return ActivatorUtilities.CreateInstance(sp, descriptor.KeyedImplementationType);
        }
        else
        {
            if (descriptor.ImplementationInstance != null)
                return descriptor.ImplementationInstance;

            if (descriptor.ImplementationFactory != null)
                return descriptor.ImplementationFactory(sp);

            if (descriptor.ImplementationType != null)
                return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }

        throw new InvalidOperationException($"Cannot create instance for {descriptor.ServiceType}");
    }
}
