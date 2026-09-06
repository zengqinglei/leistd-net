using Microsoft.Extensions.DependencyInjection;
using Leistd.DependencyInjection.Registration;
using Leistd.DependencyInjection.Abstractions;

namespace Leistd.DependencyInjection.Extensions;

/// <summary>
/// 提供服务注册回调和校验扩展。
/// </summary>
public static class ServiceCollectionRegistrationExtensions
{
    /// <summary>
    /// 注册在构建 <see cref="IServiceProvider"/> 时执行的描述符回调。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="registrationAction">描述符回调。</param>
    /// <returns>原服务集合。</returns>
    public static IServiceCollection OnServiceRegistered(
        this IServiceCollection services,
        Action<IOnServiceRegisteredContext> registrationAction)
    {
        GetOrCreateRegistrationActionList(services).Add(registrationAction);
        return services;
    }

    /// <summary>
    /// 获取已注册的描述符回调。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>回调列表。</returns>
    public static ServiceRegistrationActionList GetRegistrationActionList(this IServiceCollection services)
    {
        return GetOrCreateRegistrationActionList(services);
    }

    /// <summary>
    /// 注册在描述符回调之前执行的服务集合校验器。
    /// </summary>
    /// <remarks>
    /// 校验器可检查完整服务集合，并应通过抛出异常阻止不满足约定的宿主启动。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="validator">接收完整服务集合的校验器。</param>
    /// <returns>原服务集合。</returns>
    public static IServiceCollection AddRegistrationValidator(
        this IServiceCollection services,
        Action<IServiceCollection> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        GetOrCreateRegistrationValidatorList(services).Add(validator);
        return services;
    }

    /// <summary>
    /// 获取已注册的服务集合校验器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>校验器列表。</returns>
    public static ServiceRegistrationValidatorList GetRegistrationValidatorList(this IServiceCollection services)
    {
        return GetOrCreateRegistrationValidatorList(services);
    }

    private static ServiceRegistrationActionList GetOrCreateRegistrationActionList(IServiceCollection services)
    {
        var actionList = services
            .FirstOrDefault(d => d.ServiceType == typeof(ServiceRegistrationActionList))
            ?.ImplementationInstance as ServiceRegistrationActionList;

        if (actionList == null)
        {
            actionList = new ServiceRegistrationActionList();

            services.AddSingleton(actionList);
        }

        return actionList;
    }

    private static ServiceRegistrationValidatorList GetOrCreateRegistrationValidatorList(IServiceCollection services)
    {
        var validatorList = services
            .FirstOrDefault(d => d.ServiceType == typeof(ServiceRegistrationValidatorList))
            ?.ImplementationInstance as ServiceRegistrationValidatorList;

        if (validatorList == null)
        {
            validatorList = new ServiceRegistrationValidatorList();
            services.AddSingleton(validatorList);
        }

        return validatorList;
    }
}
