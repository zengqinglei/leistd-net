using Castle.DynamicProxy;
using Leistd.DependencyInjection;
using Leistd.DynamicProxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.DependencyInjection.DynamicProxy;

/// <summary>
/// 支持 DynamicProxy 拦截器织入的服务注册回调工厂。
/// </summary>
public class DynamicProxyServiceRegistrationCallbackFactory : ServiceRegistrationCallbackFactory
{
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

    private static void ApplyInterceptors(
        IServiceCollection services,
        ServiceDescriptor descriptor,
        List<Type> interceptorTypes)
    {
        var index = services.IndexOf(descriptor);
        if (index < 0)
            return;

        services[index] = ServiceDescriptor.Describe(
            descriptor.ServiceType,
            sp =>
            {
                var instance = CreateOriginalInstance(descriptor, sp);
                var proxyGenerator = sp.GetRequiredService<IProxyGenerator>();
                var resolvedInterceptors = interceptorTypes
                    .Select(t => sp.GetRequiredService(t))
                    .OrderBy(interceptor => interceptor is BaseAsyncInterceptor asyncInterceptor
                        ? asyncInterceptor.Order
                        : 0)
                    .ToArray();
                var interceptors = resolvedInterceptors
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
            },
            descriptor.Lifetime);
    }

    private static IInterceptor ToCastleInterceptor(object interceptor) => interceptor switch
    {
        IInterceptor synchronous => synchronous,
        IAsyncInterceptor asynchronous => asynchronous.ToInterceptor(),
        _ => throw new InvalidOperationException(
            $"Interceptor '{interceptor.GetType().FullName}' must implement Castle IInterceptor or IAsyncInterceptor.")
    };

    private static object CreateOriginalInstance(ServiceDescriptor descriptor, IServiceProvider sp)
    {
        if (descriptor.ImplementationInstance != null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory != null)
            return descriptor.ImplementationFactory(sp);

        if (descriptor.ImplementationType != null)
            return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);

        throw new InvalidOperationException($"Cannot create instance for {descriptor.ServiceType}");
    }
}
