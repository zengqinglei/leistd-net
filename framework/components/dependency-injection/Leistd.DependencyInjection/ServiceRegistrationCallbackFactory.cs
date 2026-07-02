using Microsoft.Extensions.DependencyInjection;

namespace Leistd.DependencyInjection;

/// <summary>
/// 服务注册回调工厂。
/// 在构建 IServiceProvider 前自动执行所有已注册的回调。
/// </summary>
public class ServiceRegistrationCallbackFactory : IServiceProviderFactory<IServiceCollection>
{
    /// <summary>
    /// 创建服务集合构建器
    /// </summary>
    public IServiceCollection CreateBuilder(IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// 构建 IServiceProvider
    /// 在构建前执行所有已注册的回调
    /// </summary>
    public IServiceProvider CreateServiceProvider(IServiceCollection services)
    {
        // 获取所有已注册的回调
        var actionList = services.GetRegistrationActionList();

        if (actionList.Any())
        {
            ProcessServiceRegistrations(services, actionList);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 处理服务注册
    /// </summary>
    private void ProcessServiceRegistrations(
        IServiceCollection services,
        ServiceRegistrationActionList actionList)
    {
        // 创建一个副本，避免在遍历时修改集合
        var descriptors = services.ToList();

        foreach (var descriptor in descriptors)
        {
            // 获取实现类型
            var implementationType = descriptor.ImplementationType
                ?? descriptor.ImplementationInstance?.GetType();

            // 跳过不支持的类型
            if (implementationType == null ||
                implementationType.IsAbstract ||
                implementationType.IsInterface)
            {
                continue;
            }

            // 创建上下文
            var context = new OnServiceRegisteredContext(
                descriptor.ServiceType,
                implementationType);

            // 执行所有回调
            foreach (var action in actionList)
            {
                action.Invoke(context);
            }

            OnRegistrationProcessed(services, descriptor, context);
        }
    }

    /// <summary>
    /// 单个服务注册回调执行完成后的扩展点。
    /// </summary>
    protected virtual void OnRegistrationProcessed(
        IServiceCollection services,
        ServiceDescriptor descriptor,
        IOnServiceRegisteredContext context)
    {
    }
}
